# 生物标志物筛选R脚本（整合LASSO/随机森林/SVM-RFE）
# 作者：AI助手 | 日期：2023-10-25

# ----------------------------
# 1. 载入必要包
# ----------------------------
if (!require("pacman"))
  install.packages("pacman")
pacman::p_load(glmnet, # LASSO回归
               randomForest, # 随机森林
               e1071, # SVM基础包
               caret, # 机器学习工具集
               pROC, # ROC分析
               tidyverse, # 数据处理
               doParallel      # 并行计算加速
)

# ----------------------------
# 2. 数据预处理函数
# ----------------------------
preprocess_data <- function(data_matrix, label_col = "condition") {
  # 分离特征矩阵和标签
  features <- as.matrix(data_matrix[, !names(data_matrix) %in% label_col])
  labels <- data_matrix[[label_col]]

  # 标准化处理（Z-score）
  scaled_features <- scale(features)

  # 创建数据框
  return(list(
    features = scaled_features,
    labels = as.factor(labels),
    original_features = colnames(features)
  ))
}

# ----------------------------
# 3. LASSO回归筛选
# ----------------------------
run_lasso <- function(X, y, nfolds = 10) {
  # 设置交叉验证参数
  cv_fit <- cv.glmnet(
    x = X,
    y = y,
    family = "binomial",
    alpha = 1,
    # L1正则化
    nfolds = nfolds,
    parallel = TRUE
  )

  # 获取最优lambda
  best_lambda <- cv_fit$lambda.min

  # 提取非零系数
  final_model <- glmnet(X,
                        y,
                        family = "binomial",
                        alpha = 1,
                        lambda = best_lambda)
  coef <- coef(final_model)

  # 提取显著特征
  selected_features <- rownames(coef)[which(coef != 0)][-1]  # 排除截距项
  return(selected_features)
}

# ----------------------------
# 4. 随机森林特征重要性
# ----------------------------
run_random_forest <- function(X, y, ntree = 500) {
  # 训练随机森林模型
  rf_model <- randomForest(
    x = X,
    y = as.factor(y),
    ntree = ntree,
    importance = TRUE,
    keep.forest = TRUE
  )

  # 获取特征重要性
  importance <- importance(rf_model, type = 2)  # 均方误差减少
  importance_df <- data.frame(feature = rownames(importance), importance = importance[, "MeanDecreaseMSE"])

  # 按重要性排序
  importance_df <- importance_df[order(-importance_df$importance), ]
  return(importance_df)
}

# ----------------------------
# 5. SVM-RFE特征筛选
# ----------------------------
run_svm_rfe <- function(X, y, ncores = 4) {
  # 设置控制参数
  ctrl <- rfeControl(
    functions = caretFuncs,
    method = "cv",
    number = 10,
    verbose = FALSE,
    returnResamp = "final",
    allowParallel = TRUE,
    verbose = FALSE
  )

  # 执行RFE
  rfe_results <- rfe(
    X,
    y,
    sizes = c(1:ncol(X)),
    # 测试所有特征数
    rfeControl = ctrl,
    method = "svmRadial",
    tuneLength = 3
  )

  # 获取最优特征
  optimal_features <- predictors(rfe_results)
  return(optimal_features)
}

# ----------------------------
# 6. 主分析流程
# ----------------------------
# 示例数据加载（替换为您的实际数据）
# data <- read.csv("your_data.csv")
# 假设数据格式：行=样本，列=特征，最后一列=标签（"Healthy"/"Disease"）

# 实际使用示例：
# 1. 加载数据
# data_matrix <- read.csv("your_data.csv")

# 2. 预处理数据
# processed_data <- preprocess_data(data_matrix)

# 3. 定义比较组（示例：健康vs疾病）
# comparison <- list(
#   "Healthy vs Disease" = list(
#     healthy = "Healthy",
#     disease = "Disease"
#   )
# )

# 4. 执行分析
# results <- run_analysis(processed_data, comparison)

# ----------------------------
# 7. 整合分析函数
# ----------------------------
run_analysis <- function(processed_data, comparison_list) {
  # 创建并行计算环境
  registerDoParallel(detectCores() - 1)

  results_list <- list()

  for (comparison_name in names(comparison_list)) {
    # 提取比较组信息
    comp <- comparison_list[[comparison_name]]
    healthy <- comp$healthy
    disease <- comp$disease

    # 创建二分类标签
    labels <- ifelse(
      processed_data$labels %in% c(healthy, disease),
      ifelse(processed_data$labels == healthy, 0, 1),
      NA
    )

    # 移除无效样本
    valid_idx <- !is.na(labels)
    X <- processed_data$features[valid_idx, ]
    y <- labels[valid_idx]

    # LASSO筛选
    lasso_features <- run_lasso(X, y)

    # 随机森林筛选
    rf_importance <- run_random_forest(X, y)

    # SVM-RFE筛选
    svm_features <- run_svm_rfe(X, y)

    # 整合结果
    final_features <- intersect(intersect(lasso_features, rf_importance$feature),
                                svm_features)

    # 保存结果
    results_list[[comparison_name]] <- list(
      comparison = comparison_name,
      lasso = lasso_features,
      random_forest = rf_importance,
      svm_rfe = svm_features,
      final_biomarkers = final_features
    )

    # 打印进度
    cat(paste(
      "\n",
      comparison_name,
      "分析完成，共筛选出",
      length(final_features),
      "个标志物\n"
    ))
  }

  return(results_list)
}

# ----------------------------
# 8. 结果可视化函数
# ----------------------------
visualize_results <- function(results) {
  # 合并所有结果
  all_features <- data.frame()

  for (res in results) {
    # 随机森林重要性可视化
    if (!is.null(res$random_forest)) {
      top_features <- head(res$random_forest, 20)
      ggplot(top_features, aes(x = reorder(feature, importance), y = importance)) +
        geom_bar(stat = "identity", fill = "steelblue") +
        coord_flip() +
        labs(
          title = paste("Top 20 Features for", res$comparison),
          x = "Feature",
          y = "Importance"
        ) +
        theme_minimal()
    }

    # 保存标志物列表
    write.csv(
      data.frame(Biomarkers = res$final_biomarkers),
      paste0(res$comparison, "_biomarkers.csv"),
      row.names = FALSE
    )
  }
}

# ----------------------------
# 9. 完整工作流程示例
# ----------------------------
# 示例数据模拟（替换为实际数据）
set.seed(123)
simulated_data <- data.frame(condition = rep(c("Healthy", "Disease1", "Disease2"), each = 50), matrix(rnorm(100 *
                                                                                                              50), nrow = 100, ncol = 50))

# 预处理
processed_data <- preprocess_data(simulated_data)

# 定义比较组
comparison_list <- list(
  "Healthy vs Disease1" = list(healthy = "Healthy", disease = "Disease1"),
  "Healthy vs Disease2" = list(healthy = "Healthy", disease = "Disease2")
)

# 执行分析
analysis_results <- run_analysis(processed_data, comparison_list)

# 可视化结果
visualize_results(analysis_results)

# ----------------------------
# 10. 扩展功能说明
# ----------------------------
# 1. 多组比较：通过扩展comparison_list支持更多疾病类型对比
# 2. 参数调优：可调整各算法的超参数（如LASSO的alpha值、RF的ntree）
# 3. 性能验证：添加以下代码进行交叉验证评估
# cv_results <- caret::train(
#   x = X,
#   y = y,
#   method = "glmnet",
#   metric = "ROC",
#   trControl = trainControl(method = "cv", number = 10)
# )
# print(cv_results)

# 差异表达分析
library(DESeq2)
dds <- DESeqDataSetFromMatrix(countData = counts,
                              colData = metadata,
                              design = ~ condition)
dds <- DESeq(dds)
res <- results(dds)

# 列线图构建
library(rms)
fit <- lrm(outcome ~ Biomarker1 + Biomarker2, data = clinical_data)
nom <- nomogram(fit, fun = plogis)
