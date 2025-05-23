# 加载必要包
library(tidyverse)
library(randomForest)
library(xgboost)
library(shap)
library(pROC)
library(caret)
library(ggthemes)

# 1. 数据预处理模块
clean_data <- function(data, id_col = "ID", class_col = "class") {
  # 数据清洗流水线
  data_clean <- data %>%
    # 删除重复样本
    distinct(!!sym(id_col), .keep_all = TRUE) %>%
    # 缺失值处理
    mutate(across(-c(!!sym(id_col), !!sym(class_col)),
                  ~ifelse(is.na(.), median(., na.rm = TRUE), .))) %>%
    # 异常值处理（IQR法）
    mutate(across(-c(!!sym(id_col), !!sym(class_col)),
                  ~ifelse(. > quantile(., 0.75, na.rm = TRUE) + 1.5*IQR(., na.rm = TRUE),
                          quantile(., 0.75, na.rm = TRUE), .),
                  .names = "{.col}_cleaned")) %>%
    # 标准化处理
    mutate(across(ends_with("_cleaned"),
                  scale,
                  .names = "std_{.col}")) %>%
    select(-ends_with("_cleaned")) %>%
    rename(original_name = !!sym(class_col)) %>%
    mutate(class = factor(original_name))

  return(data_clean)
}

# 2. 特征筛选模块
feature_selection <- function(data, class_col = "class", p_threshold = 0.05) {
  # 双重筛选机制（ANOVA + 变异系数）
  significant_features <- data %>%
    select(-c(ID, class)) %>%
    pivot_longer(-original_name, names_to = "feature", values_to = "value") %>%
    group_by(feature) %>%
    do({
      # ANOVA检验
      anova_result <- aov(value ~ class, data = .)
      p_value <- summary(anova_result)[[1]][5]

      # 变异系数过滤（保留前50%高变异特征）
      cv <- sd(.$value)/mean(.$value)

      data.frame(
        feature = unique(.$feature),
        p_value = p_value,
        cv = cv,
        significant = p_value < p_threshold & cv > median(.$cv)
      )
    }) %>%
    filter(significant) %>%
    arrange(p_value) %>%
    pull(feature)

  return(significant_features)
}

# 3. 模型训练模块
train_models <- function(data, features, class_col = "class") {
  # 分层抽样
  set.seed(123)
  split_idx <- createDataPartition(data$class, p = 0.7, list = FALSE)

  # 训练XGBoost
  xgb_model <- xgboost(
    data = as.matrix(data[split_idx, features]),
    label = as.numeric(data$class[split_idx]) - 1,
    nrounds = 100,
    objective = "binary:logistic",
    eval_metric = "logloss"
  )

  # 训练随机森林
  rf_model <- randomForest(
    x = data[split_idx, features],
    y = as.factor(data$class[split_idx]),
    ntree = 500,
    importance = TRUE
  )

  return(list(
    xgb = xgb_model,
    rf = rf_model,
    train_idx = split_idx,
    test_idx = -split_idx
  ))
}

# 4. 特征重要性分析
compute_importance <- function(models, test_data, features) {
  importance_list <- list()

  # XGBoost SHAP值计算
  shap_values <- shap::shap.values(models$xgb, as.matrix(test_data[, features]))
  importance_list$shap <- shap_values %>%
    bind_rows() %>%
    group_by(feature) %>%
    summarise(mean_abs_shap = mean(abs(value))) %>%
    arrange(desc(mean_abs_shap))

  # 随机森林重要性
  importance_list$rf <- varImp(models$rf) %>%
    data.frame() %>%
    mutate(feature = rownames(.)) %>%
    arrange(desc(Overall))

  return(importance_list)
}

# 5. 性能评估模块
evaluate_performance <- function(models, data, features, class_col = "class") {
  # 预测结果
  predictions <- list()
  predictions$xgb <- predict(models$xgb, as.matrix(data[, features]))
  predictions$rf <- predict(models$rf, newdata = data[, features], type = "prob")[, 2]

  # 计算性能指标
  roc_results <- lapply(predictions, function(pred) {
    roc_obj <- roc(data[[class_col]], pred)
    data.frame(
      AUC = auc(roc_obj),
      Accuracy = mean(data[[class_col]] == factor(ifelse(pred > 0.5, 2, 1))),
      Sensitivity = sensitivity(roc_obj, specificities = 0.95),
      Specificity = specificity(roc_obj, sensitivities = 0.95)
    )
  })

  return(bind_rows(roc_results, .id = "Model"))
}

# 6. 可视化模块
visualize_results <- function(importance_list, performance, output_path) {
  # 特征重要性热图
  ggplot(importance_list$shap, aes(x = reorder(feature, mean_abs_shap), y = mean_abs_shap)) +
    geom_col(fill = "#2c7bb6") +
    coord_flip() +
    labs(title = "SHAP Feature Importance", x = "Feature", y = "Mean Absolute SHAP") +
    theme_minimal(base_size = 12) +
    ggsave(file.path(output_path, "shap_importance.png"), width = 12, height = 8)

  # 模型性能对比
  performance %>%
    pivot_longer(-Model, names_to = "Metric", values_to = "Value") %>%
    ggplot(aes(x = Model, y = Value, fill = Model)) +
    geom_col(position = "dodge") +
    facet_wrap(~Metric, scales = "free") +
    labs(title = "Model Performance Comparison") +
    theme_minimal() +
    ggsave(file.path(output_path, "model_comparison.png"), width = 12, height= 8)
}

# 7. 主执行函数
run_biomarker_analysis <- function(input_file, output_dir, class_col = "class") {
  # 数据加载
  raw_data <- read.csv(input_file, check.names = FALSE)

  # 数据清洗
  cleaned_data <- clean_data(raw_data)

  # 特征筛选
  selected_features <- feature_selection(cleaned_data)

  # 模型训练
  models <- train_models(cleaned_data, selected_features)

  # 特征重要性计算
  importance <- compute_importance(models, cleaned_data[-models$test_idx, ], selected_features)

  # 性能评估
  performance <- evaluate_performance(models, cleaned_data[-models$train_idx, ], selected_features)

  # 可视化输出
  visualize_results(importance, performance, output_dir)

  # 保存关键结果
  write.csv(data.frame(Feature = selected_features),
            file.path(output_dir, "selected_features.csv"), row.names = FALSE)

  return(performance)
}

# 使用示例
# run_biomarker_analysis(
#   input_file = "clinical_data.csv",
#   output_dir = "analysis_results",
#   class_col = "class"
# )
# 多组学数据扩展
run_biomarker_analysis(
  input_file = "multiomics_data.csv",
  class_col = "disease_status",
  additional_steps = list(
    # 添加代谢组学特征
    add_metabolomics = TRUE,
    # 添加影像组学特征
    add_radiomics = TRUE
  )
)


# XGBoost超参数优化
xgb_params <- list(
  max_depth = 6,
  eta = 0.3,
  gamma = 0,
  min_child_weight = 1,
  subsample = 0.8,
  colsample_bytree = 0.8,
  objective = "binary:logistic"
)
