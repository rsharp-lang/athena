# 发挥你的资深级的数据分析工程师的能力，帮助我完成数据分析任务：现在假设我有一个数据矩阵，行是病人样本，列是生物学特征。矩阵中第一列为样本的id，这个矩阵中第二个列名字叫做class的列，
# 包含对应的行的标签：有健康正常人标签，有其他的几种病理性标签，其他的列则是采样的生化指标检测结果。请编写R脚本代码，帮助我按照这篇文章中的思路方法来找出健康人和各种不同的病理性标签
# 人群之间的biomarker，并绘制结果进行可视化。每一个步骤的分析应该是使用一个函数来表示。

# 加载必要包
library(ggplot2)
library(glmnet)
library(randomForest)
library(e1071)
library(caret)
library(pheatmap)
library(ggcorrplot)
library(dplyr)

# 1. 数据预处理函数
preprocess_data <- function(data, remove_na = TRUE, normalize = TRUE) {
  # 数据清洗
  data_clean <- data %>%
    filter(!is.na(class)) %>%
    select(-id)  # 移除ID列

  colnames(data_clean) <- trimws(colnames(data_clean));

  for(name in colnames(data_clean)) {
    if (name != "class") {
      v <- as.numeric(data_clean[,name]);
      v[is.na(v)] <- mean(v[!is.na(v)])/2;

      data_clean[,name] = v;
    }
  }

  # 分离特征和标签
  X <- as.matrix(data_clean %>% select(-class))
  y <- as.factor(data_clean$class)

  # 特征标准化
  if(normalize) {
    X <- scale(X)
  }

  return(list(X = X, y = y))
}

# 2. LASSO特征选择函数
run_lasso <- function(X, y, lambda = 0.01) {
  # 交叉验证确定最优lambda
  cv_fit <- cv.glmnet(X, y, family = "binomial", alpha = 1)

  # 提取最优模型
  best_model <- glmnet(X, y, family = "binomial", alpha = 1, lambda = lambda)

  # 提取非零系数特征
  coefs <- coef(best_model, s = lambda)
  selected_features <- rownames(coefs)[which(coefs != 0)][-1]  # 排除截距项

  return(list(
    features = selected_features,
    model = best_model,
    cv_error = min(cv_fit$cvm)
  ))
}

# 3. 随机森林特征重要性
run_random_forest <- function(X, y, ntree = 500) {
  set.seed(123)
  rf_model <- randomForest(X, y, ntree = ntree, importance = TRUE)

  # 提取重要性排名
  importance_df <- data.frame(
    feature = colnames(X),
    importance = rf_model$importance[, "MeanDecreaseGini"],
    stringsAsFactors = FALSE
  ) %>%
    arrange(desc(importance))

  return(list(
    features = importance_df$feature[importance_df$importance > 0.5],
    model = rf_model,
    oob_error = rf_model$err.rate[nrow(rf_model$err.rate), "OOB"]
  ))
}

# 4. SVM-RFE特征选择
run_svm_rfe <- function(X, y, n_features = 5, metric = "Accuracy", kernel = "radial",...) {
  # 核方法映射表
  method_map <- list(
    radial = "svmRadial",
    linear = "svmLinear",
    polynomial = "svmPoly",
    sigmoid = "svmSigmoid"
  )

  # 参数模板（扩展默认值）
  kernel_params <- list(
    radial = list(gamma = 1/sqrt(ncol(X)), cost = 1),
    linear = list(cost = 10),
    polynomial = list(degree = 3, scale = TRUE, coef0 = 0, cost = 1),
    sigmoid = list(gamma = 1/sqrt(ncol(X)), coef0 = 0, cost = 1)
  )

  # 参数合并与验证
  svm_params <- modifyList(kernel_params[[kernel]], list(...))

  # 生成调参网格
  tuneGrid <- switch(kernel,
                     "radial" = expand.grid(C = svm_params$cost, sigma = svm_params$gamma),
                     "linear" = expand.grid(C = svm_params$cost),
                     "polynomial" = expand.grid(C = svm_params$cost, degree = svm_params$degree, scale = svm_params$scale),
                     "sigmoid" = expand.grid(C = svm_params$cost, sigma = svm_params$gamma, coef0 = svm_params$coef0)
  )

  # 设置特征筛选控制参数
  ctrl <- rfeControl(
    functions = caretFuncs,
    method = "cv",
    number = 10,
    verbose = FALSE,
    returnResamp = "all",
    # 关键参数：强制遍历所有特征数量
    saveDetails = TRUE
  )


  # 定义特征筛选过程
  rfe_results <- rfe(
    X, y,
    sizes = 1:n_features,
    rfeControl = ctrl,
    method = method_map[[kernel]],
    metric = metric,
    tuneLength = 5,
    tuneGrid = tuneGrid,
    preProcess = c("center", "scale"),
    importance = TRUE
  )

  return(list(
    features = predictors(rfe_results),
    model = rfe_results$fit,
    error = rfe_results$resample$RMSE
  ))
}

# 5. 模型集成与验证
ensemble_model <- function(X, y, selected_features) {
  # 构建列线图模型
  factors = paste("`",paste(selected_features, collapse = "`+`"),"`", sep = "");

  print("view factor list:");
  print(factors);

  formula <- as.formula(paste("class ~ ", factors ));
  dX = as.data.frame(X);
  dX[,"class"] <- y;
  nomogram_model <- glm(formula, family = "binomial", data = dX)

  # 交叉验证评估
  cv_results <- cv.glmnet(X[, selected_features], y, family = "binomial", alpha = 0)
  roc_auc <- max(cv_results$glmnet.fit$dev.ratio) * 100

  return(list(
    nomogram = nomogram_model,
    auc = roc_auc,
    coefficients = coef(nomogram_model),
    formula = formula,
    dX = dX
  ))
}

# 6. 可视化模块
visualize_results <- function(results, X, y) {
  nomogram_model <- results$nomogram;
  auc <- results$auc;
  coefficients <- results$coefficients;
  dX = results$dX;
  formula = results$formula;

  pdf(file = "./roc.pdf");
  library(pROC)
  # 获取预测概率
  pred_prob <- predict(nomogram_model, type = "response")
  # 绘制ROC曲线
  roc_obj <- roc(y, pred_prob)
  plot(roc_obj, print.auc = TRUE, auc.polygon = TRUE)
  # 添加交叉验证的AUC值
  text(0.5, 0.3, paste("CV AUC:", round(auc, 2)), col = "red");
  dev.off();

  # library(rms)
  # 转换为rms包数据格式
  # ddist <- datadist(as.data.frame(X))
  # options(datadist = "ddist")
  # 绘制校准曲线
  # cal <- calibrate(nomogram_model, method = "boot", B = 1000)
  # plot(cal)

  pdf(file = "./feature_importance.pdf");
  library(ggplot2)
  coef_df <- data.frame(
    Feature = names(coefficients(nomogram_model))[-1],
    Importance = abs(coefficients(nomogram_model)[-1])
  )
  ggplot(coef_df, aes(x = reorder(Feature, Importance), y = Importance)) +
    geom_col(fill = "#87CEEB") +
    coord_flip() +
    labs(title = "Logistic Regression Feature Importance", x = "")
  dev.off();

  # library(fastshap)
  # shap_values <- explain(nomogram_model, X = X[, selected_features])
  # autoplot(shap_values, type = "importance")

  pdf(file = "./nomogram.pdf");
  library(rms)
  # 重构模型为rms格式
  lrm_model <- lrm(formula, data = dX, x = TRUE, y = TRUE)
  nom <- nomogram(lrm_model, fun = plogis, funlabel = "Risk Probability")
  plot(nom)
  dev.off();

  # library(rmda)
  # dca_data <- decision_curve(formula, data = dX, family = binomial)
  # plot_decision_curve(dca_data, curve.names = "Our Model")
}

# 主执行流程
main <- function(file_path) {
  # 1. 加载数据
  data <- read.csv(file_path);

  class_filter = c("正常妊娠孕妇","复发性流产");

  # 2. 数据预处理
  preprocessed <- preprocess_data(data[data$class %in% class_filter,]);
  X <- preprocessed$X
  y <- preprocessed$y

  # 3. 特征选择
  lasso_result <- run_lasso(X, y)
  rf_result <- run_random_forest(X, y)
  svm_result <- run_svm_rfe(X, y)

  # 4. 模型集成
  ensemble_result <- ensemble_model(X, y,
                                    intersect( intersect(lasso_result$features, rf_result$features), svm_result$features))

  # 5. 可视化
  visualize_results(ensemble_result, X, y)

  return(ensemble_result)
}

# 运行示例
# result <- main("your_data.csv")
# 添加多组学验证模块
multiomics_validation <- function(selected_features, clinical_data, omics_data) {
  # 外周血验证
  pbmc_validation <- wilcox.test(clinical_data$pbmc_count ~ clinical_data$class)

  # 免疫组化验证
  ica_validation <- glm(ica_expression ~ class, data = omics_data)

  return(list(
    pbmc_pvalue = pbmc_validation$p.value,
    ica_coefficient = coef(ica_validation)[2]
  ))
}

# 添加临床决策曲线分析
clinical_dca <- function(probabilities, labels) {
  library(DCA);

  dca_result <- DCA(data.frame(prob = probabilities, outcome = labels),
                    outcome.name = "outcome",
                    treat.name = "Treatment",
                    threshold.param = seq(0, 1, 0.01))

  plot(dca_result)
}
