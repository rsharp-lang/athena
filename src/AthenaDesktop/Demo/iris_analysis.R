#!/usr/bin/env Rscript
# ==============================================================================
#  Athena 生成式界面 Demo：经典 Iris 数据集分析脚本
#
#  分析内容：
#    1. kmeans 聚类分析         -> 聚类散点图、簇剖面图、簇中心/簇概要表格
#    2. PCA 主成分降维分析      -> 方差碎石图、PCA 得分图与双标图、方差/得分表格
#    3. 线性回归拟合            -> 回归拟合图、回归系数表与模型摘要
#    4. 数据可视化              -> 变量散点图矩阵
#
#  参数传递约定：
#    宿主通过命令行以 key=value 的形式把界面上的参数传进来，例如：
#      Rscript iris_analysis.R out_dir="<dir>" k=3 palette=rainbow point_color=#3366CC
#    本脚本通过 commandArgs(trailingOnly = TRUE) 读取这些参数。
#
#  结果输出约定：
#    所有的结果文件（png 图片、csv 表格、txt 摘要）都会写入 out_dir 指定的目录，
#    宿主会自动扫描该目录，把图片、表格与文本结果回传到动态生成的 html 界面上。
#
#  依赖说明：
#    只使用 base R（stats / graphics / grDevices），不需要安装任何第三方包。
# ==============================================================================

# ---------------------------------------------------------------- 参数解析 ----
parse_args <- function(argv) {
  kv <- list()
  for (a in argv) {
    a <- sub("^--", "", trimws(a))
    if (grepl("=", a, fixed = TRUE)) {
      key <- trimws(sub("=.*$", "", a))
      val <- trimws(sub("^[^=]*=", "", a))
      if (nchar(key) > 0) kv[[key]] <- val
    }
  }
  kv
}

arg_str <- function(args, key, default) {
  v <- args[[key]]
  if (is.null(v) || is.na(v) || !nzchar(as.character(v))) default else as.character(v)
}

arg_num <- function(args, key, default) {
  v <- suppressWarnings(as.numeric(arg_str(args, key, "")))
  if (is.na(v)) default else v
}

arg_bool <- function(args, key, default) {
  v <- tolower(arg_str(args, key, ""))
  if (v %in% c("true", "1", "yes", "y", "t")) {
    TRUE
  } else if (v %in% c("false", "0", "no", "n", "f")) {
    FALSE
  } else {
    default
  }
}

args <- parse_args(commandArgs(trailingOnly = TRUE))

out_dir       <- arg_str(args,  "out_dir",        getwd())
input_file    <- arg_str(args,  "input",          "")
k             <- as.integer(max(2, min(10, round(arg_num(args, "k", 3)))))
seed          <- as.integer(arg_num(args, "seed", 42))
scale_data    <- arg_bool(args, "scale_data",     TRUE)
species_txt   <- arg_str(args,  "species_filter", "")
palette_name  <- arg_str(args,  "palette",        "rainbow")
point_color   <- arg_str(args,  "point_color",    "#2E86AB")
point_size    <- arg_num(args,  "point_size",     1.35)
point_alpha   <- max(0.05, min(1, arg_num(args, "point_alpha", 0.85)))
lm_x          <- arg_str(args,  "lm_x",           "Sepal.Length")
lm_y          <- arg_str(args,  "lm_y",           "Petal.Length")
plot_w        <- as.integer(max(320, arg_num(args, "plot_width",  1000)))
plot_h        <- as.integer(max(240, arg_num(args, "plot_height", 760)))
plot_format   <- arg_str(args,  "plot_format",    "png")
main_title    <- arg_str(args,  "title",          "Iris 数据集综合分析")

if (!dir.exists(out_dir)) dir.create(out_dir, recursive = TRUE, showWarnings = FALSE)

cat("===========================================================\n")
cat("Athena 生成式界面 Demo - Iris 数据集分析\n")
cat("R 版本:", R.version.string, "\n")
cat("结果输出目录:", out_dir, "\n")
cat("运行参数: k =", k, ", seed =", seed, ", scale =", scale_data,
    ", palette =", palette_name, ", lm =", lm_y, "~", lm_x, "\n")
cat("===========================================================\n")

# ------------------------------------------------------------ 绘图设备工具 ----
use_svg <- identical(tolower(plot_format), "svg")

if (use_svg) {
  probe <- file.path(out_dir, ".__svg_probe.svg")
  ok <- tryCatch({
    svg(probe, width = 1, height = 1)
    dev.off()
    TRUE
  }, error = function(e) FALSE)
  if (!isTRUE(ok)) use_svg <- FALSE
  unlink(probe)
}

plot_ext <- if (use_svg) "svg" else "png"

plot_path <- function(name) file.path(out_dir, sprintf("%s.%s", name, plot_ext))

open_dev <- function(path, w, h) {
  if (use_svg) {
    svg(path, width = w / 96, height = h / 96)
  } else {
    ok <- tryCatch({
      png(path, width = w, height = h, res = 96, type = "cairo")
      TRUE
    }, error = function(e) FALSE)
    if (!isTRUE(ok)) png(path, width = w, height = h, res = 96)
  }
}

# ---------------------------------------------------------------- 调色板 ----
make_palette <- function(name, n) {
  key <- tolower(name)
  pal <- switch(key,
    "rainbow"        = rainbow(n),
    "heat.colors"    = heat.colors(n),
    "terrain.colors" = terrain.colors(n),
    "topo.colors"    = topo.colors(n),
    "cm.colors"      = cm.colors(n),
    "hcl.dark3"      = hcl.colors(n, "Dark 3"),
    "hcl.sunset"     = hcl.colors(n, "SunsetDark"),
    "greys"          = grey.colors(n, start = 0.85, end = 0.12),
    rainbow(n)
  )
  if (is.null(pal)) pal <- rainbow(n)
  adjustcolor(pal, alpha.f = 0.92)
}

# -------------------------------------------------------------- 数据载入 ----
cat("[1/6] 正在载入数据...\n")

df <- if (nzchar(input_file) && file.exists(input_file)) {
  cat("      从文件载入:", input_file, "\n")
  read.csv(input_file, stringsAsFactors = FALSE, check.names = TRUE)
} else {
  if (nzchar(input_file)) {
    cat("      警告：输入文件不存在，改用内置的 iris 数据集\n")
  } else {
    cat("      未指定输入文件，使用内置的 iris 数据集\n")
  }
  iris
}

if (!is.data.frame(df) || nrow(df) < 3) {
  stop("数据集为空或者样本数量不足，无法进行分析")
}

has_species <- "Species" %in% names(df)

if (has_species && nzchar(species_txt)) {
  keep <- trimws(unlist(strsplit(species_txt, "[,;]")))
  keep <- keep[nzchar(keep) > 0]
  if (length(keep) > 0) {
    df <- df[df$Species %in% keep, , drop = FALSE]
    cat("      按物种筛选后剩余样本:", nrow(df), "\n")
  }
}

num_cols <- names(df)[vapply(df, is.numeric, logical(1))]

if (length(num_cols) < 2) {
  stop("数据集中至少需要 2 个数值型变量才能进行分析")
}

# 缺失值用列均值填充
for (cn in num_cols) {
  bad <- is.na(df[[cn]])
  if (any(bad)) df[[cn]][bad] <- mean(df[[cn]], na.rm = TRUE)
}

x <- as.matrix(df[, num_cols, drop = FALSE])

if (has_species) {
  sp <- droplevels(factor(df$Species))
} else {
  sp <- NULL
}

species_cols <- if (has_species) make_palette(palette_name, nlevels(sp)) else NULL

cat("      样本数:", nrow(df), " 数值变量数:", length(num_cols), "\n")

# ------------------------------------------------------------ kmeans 聚类 ----
cat("[2/6] 正在执行 kmeans 聚类分析...\n")

set.seed(seed)
x_km <- if (scale_data) scale(x) else x
km <- kmeans(x_km, centers = k, nstart = 25, iter.max = 200)
cl <- factor(km$cluster, levels = seq_len(k))
cluster_cols <- make_palette(palette_name, k)

# ---------------------------------------------------------------- PCA 分析 ----
cat("[3/6] 正在执行 PCA 主成分分析...\n")

pca <- prcomp(x, center = TRUE, scale. = scale_data)
pca_imp <- summary(pca)$importance
pca_var <- data.frame(
  PC = colnames(pca_imp),
  std_dev = round(as.numeric(pca_imp[1, ]), 6),
  variance_ratio = round(as.numeric(pca_imp[2, ]), 6),
  cumulative_ratio = round(as.numeric(pca_imp[3, ]), 6)
)
pc12_var <- round(sum(as.numeric(pca_imp[2, 1:min(2, ncol(pca_imp))])) * 100, 2)

# ------------------------------------------------------------ 线性回归拟合 ----
cat("[4/6] 正在执行线性回归拟合...\n")

if (!(lm_x %in% names(df))) lm_x <- num_cols[1]
if (!(lm_y %in% names(df)) || identical(lm_y, lm_x)) lm_y <- num_cols[min(2, length(num_cols))]

fit_formula <- as.formula(paste(lm_y, "~", lm_x))
fit <- lm(fit_formula, data = df)
fit_summary <- summary(fit)
r_squared <- round(fit_summary$r.squared, 4)
adj_r_squared <- round(fit_summary$adj.r.squared, 4)
p_value <- signif(fit_summary$coefficients[2, 4], 4)
lm_label <- paste0(
  lm_y, " = ", round(coef(fit)[1], 4),
  ifelse(coef(fit)[2] >= 0, " + ", " - "),
  abs(round(coef(fit)[2], 4)), " * ", lm_x,
  "   (R² = ", r_squared, ")"
)

# -------------------------------------------------------------- 结果可视化 ----
cat("[5/6] 正在绘制结果图...\n")

# 图 1：变量散点图矩阵 -------------------------------------------------------
open_dev(plot_path("01_pairs_overview"), plot_w, plot_h)
par(mar = c(1.2, 1.2, 2.6, 1.2), oma = c(2.5, 2.5, 0.6, 0.6), mgp = c(1.6, 0.5, 0), xpd = NA)

panel_cor <- function(u, v, ...) {
  points(u, v, pch = 19, cex = point_size * 0.72,
         col = point_col_vec[match(v, u)])
  invisible(NULL)
}
point_col_vec <- if (has_species) {
  adjustcolor(species_cols[as.integer(sp)], alpha.f = point_alpha)
} else {
  adjustcolor(rep(point_color, nrow(x)), alpha.f = point_alpha)
}

pairs(x, main = paste0(main_title, " — 变量散点图矩阵"),
      col = point_col_vec, pch = 19, cex = point_size * 0.72,
      lower.panel = NULL, gap = 0.45, font.main = 1, cex.main = 1.15)

if (has_species) {
  legend("bottom", inset = -0.055, legend = levels(sp), fill = species_cols,
         horiz = TRUE, bty = "n", cex = 0.9, x.intersp = 0.6)
}
dev.off()

# 图 2：聚类结果 -------------------------------------------------------------
open_dev(plot_path("02_kmeans_clusters"), plot_w, plot_h)
layout(matrix(1:4, 2, 2, byrow = TRUE))
par(mar = c(4, 4, 3, 1.4), mgp = c(2, 0.7, 0), xpd = NA)

v1 <- num_cols[1]
v2 <- num_cols[min(2, length(num_cols))]
v3 <- num_cols[min(3, length(num_cols))]
v4 <- num_cols[min(4, length(num_cols))]

plot(df[[v1]], df[[v2]], col = adjustcolor(cluster_cols[cl], alpha.f = point_alpha),
     pch = 19, cex = point_size, xlab = v1, ylab = v2,
     main = paste0("聚类结果 (", v1, " × ", v2, ")"))
grid(col = "grey88", lty = 3)
centers_raw <- if (scale_data) sweep(km$centers, 2, attr(x_km, "scaled:scale"), "*") else km$centers
centers_raw <- sweep(centers_raw, 2, attr(x_km, "scaled:center"), "+")
points(centers_raw[, v1], centers_raw[, v2], pch = 8, cex = 2, lwd = 2.4, col = "#0E1B2A")

plot(df[[v3]], df[[v4]], col = adjustcolor(cluster_cols[cl], alpha.f = point_alpha),
     pch = 19, cex = point_size, xlab = v3, ylab = v4,
     main = paste0("聚类结果 (", v3, " × ", v4, ")"))
grid(col = "grey88", lty = 3)
points(centers_raw[, v3], centers_raw[, v4], pch = 8, cex = 2, lwd = 2.4, col = "#0E1B2A")

barplot(as.integer(table(cl)), names.arg = paste0("簇", seq_len(k)),
        col = cluster_cols, border = NA,
        main = "各簇样本数", ylab = "样本数", xlab = "聚类簇")
grid(nx = NA, ny = NULL, col = "grey88", lty = 3)

barplot(km$withinss, names.arg = paste0("簇", seq_len(k)),
        col = adjustcolor(cluster_cols, alpha.f = 0.75), border = NA,
        main = "簇内平方和 (withinss)", ylab = "withinss", xlab = "聚类簇")
grid(nx = NA, ny = NULL, col = "grey88", lty = 3)

mtext(paste0(main_title, " — kmeans 聚类 (k = ", k, ")"), outer = TRUE, line = -1.6, font = 2, cex = 1.1)
dev.off()

# 图 3：PCA 方差解释 ---------------------------------------------------------
open_dev(plot_path("03_pca_variance"), plot_w, plot_h)
par(mar = c(5, 5, 3.4, 5), mgp = c(2.6, 0.8, 0), xpd = NA)

np <- nrow(pca_var)
bp <- barplot(pca_var$variance_ratio * 100, col = make_palette(palette_name, np),
              border = NA, names.arg = pca_var$PC, ylim = c(0, max(pca_var$variance_ratio * 100) * 1.25),
              xlab = "主成分", ylab = "方差解释比例 (%)",
              main = paste0(main_title, " — PCA 方差解释度"))
grid(nx = NA, ny = NULL, col = "grey88", lty = 3)
text(bp, pca_var$variance_ratio * 100, labels = paste0(round(pca_var$variance_ratio * 100, 1), "%"),
     pos = 3, cex = 0.82, col = "#334155")
par(new = TRUE)
plot(bp, pca_var$cumulative_ratio * 100, type = "b", pch = 17, lty = 2, lwd = 2,
     col = "#0E1B2A", axes = FALSE, xlab = "", ylab = "", ylim = c(0, 105))
axis(4, col = "#0E1B2A", col.axis = "#0E1B2A", mgp = c(2.6, 0.8, 0))
mtext("累计方差解释比例 (%)", side = 4, line = 2.6, col = "#0E1B2A")
dev.off()

# 图 4：PCA 得分与双标图 -----------------------------------------------------
open_dev(plot_path("04_pca_scores"), plot_w, plot_h)
layout(matrix(1:2, 1, 2))
par(mar = c(4.4, 4.4, 3.2, 1.4), mgp = c(2.2, 0.7, 0), xpd = NA)

score_cols <- if (has_species) {
  adjustcolor(species_cols[as.integer(sp)], alpha.f = point_alpha)
} else {
  adjustcolor(cluster_cols[cl], alpha.f = point_alpha)
}

plot(pca$x[, 1], pca$x[, 2], col = score_cols, pch = 19, cex = point_size,
     xlab = paste0("PC1 (", round(pca_var$variance_ratio[1] * 100, 1), "%)"),
     ylab = paste0("PC2 (", round(pca_var$variance_ratio[2] * 100, 1), "%)"),
     main = "PCA 得分图")
grid(col = "grey88", lty = 3)
abline(h = 0, v = 0, col = "grey70", lty = 2)
if (has_species) {
  legend("topright", legend = levels(sp), fill = species_cols, bty = "n", cex = 0.85, inset = -0.02)
} else {
  legend("topright", legend = paste0("簇", seq_len(k)), fill = cluster_cols, bty = "n", cex = 0.85, inset = -0.02)
}

biplot(pca, col = c("grey55", adjustcolor(point_color, alpha.f = point_alpha)),
       cex = 0.78, main = "PCA 双标图 (变量载荷)", xlab = "PC1", ylab = "PC2")
dev.off()

# 图 5：线性回归拟合 ---------------------------------------------------------
open_dev(plot_path("05_regression_fit"), plot_w, plot_h)
par(mar = c(4.8, 4.8, 3.4, 1.4), mgp = c(2.4, 0.8, 0), xpd = NA)

xs <- df[[lm_x]]
ys <- df[[lm_y]]
reg_cols <- if (has_species) {
  adjustcolor(species_cols[as.integer(sp)], alpha.f = point_alpha)
} else {
  adjustcolor(rep(point_color, nrow(df)), alpha.f = point_alpha)
}

plot(xs, ys, col = reg_cols, pch = 19, cex = point_size,
     xlab = lm_x, ylab = lm_y, main = paste0(main_title, " — 线性回归拟合"))
grid(col = "grey88", lty = 3)

nd <- data.frame(seq(min(xs), max(xs), length.out = 200))
names(nd) <- lm_x
pr <- tryCatch(predict(fit, newdata = nd, interval = "confidence"), error = function(e) NULL)

if (!is.null(pr)) {
  ord <- order(nd[[1]])
  polygon(c(nd[[1]][ord], rev(nd[[1]][ord])), c(pr[ord, "lwr"], rev(pr[ord, "upr"])),
          col = adjustcolor(point_color, alpha.f = 0.16), border = NA)
  lines(nd[[1]][ord], pr[ord, "fit"], col = point_color, lwd = 2.6)
}

points(xs, ys, col = reg_cols, pch = 19, cex = point_size)
mtext(lm_label, side = 1, line = 3.3, cex = 0.92, col = "#1B5E7E")

if (has_species) {
  legend("topleft", legend = levels(sp), fill = species_cols, bty = "n", cex = 0.85, inset = -0.02)
}
dev.off()

# 图 6：簇剖面 ---------------------------------------------------------------
open_dev(plot_path("06_cluster_profile"), plot_w, plot_h)
par(mar = c(6, 4.6, 3.4, 1.4), mgp = c(2.4, 0.8, 0), xpd = NA)

barplot(t(scale(km$centers)), beside = TRUE, col = cluster_cols, border = NA,
        names.arg = paste0("簇", seq_len(k)), xlab = "聚类簇",
        ylab = "标准化后的簇中心", main = "各聚类的变量剖面")
grid(nx = NA, ny = NULL, col = "grey88", lty = 3)
abline(h = 0, col = "grey40")
legend("topright", legend = num_cols, fill = make_palette(palette_name, length(num_cols)),
       bty = "n", cex = 0.82, inset = -0.02)
dev.off()

# ------------------------------------------------------------ 结果文件输出 ----
cat("[6/6] 正在写出结果表格...\n")

# 数据预览
write.csv(head(df, 50), file.path(out_dir, "data_preview.csv"), row.names = FALSE)

# 簇中心
centers_out <- as.data.frame(centers_raw)
centers_out$cluster <- seq_len(k)
centers_out <- centers_out[, c("cluster", setdiff(names(centers_out), "cluster"))]
write.csv(centers_out, file.path(out_dir, "cluster_centers.csv"), row.names = FALSE)

# 聚类概要
size_tbl <- as.integer(table(cl))
comp <- if (has_species) {
  tab <- table(cl, sp)
  apply(tab, 1, function(row) paste(names(row), row, sep = ":", collapse = "  "))
} else {
  rep("", k)
}
cluster_summary <- data.frame(
  cluster = seq_len(k),
  size = size_tbl,
  ratio = round(size_tbl / sum(size_tbl), 4),
  withinss = round(km$withinss, 4),
  species_composition = comp,
  stringsAsFactors = FALSE
)
write.csv(cluster_summary, file.path(out_dir, "cluster_summary.csv"), row.names = FALSE)

# PCA 方差
write.csv(pca_var, file.path(out_dir, "pca_variance.csv"), row.names = FALSE)

# PCA 得分
scores_out <- as.data.frame(round(pca$x[, 1:min(3, ncol(pca$x)), drop = FALSE], 6))
scores_out$Species <- if (has_species) as.character(sp) else "-"
scores_out$Cluster <- as.integer(cl)
write.csv(scores_out, file.path(out_dir, "pca_scores.csv"), row.names = FALSE)

# 回归系数
coef_out <- as.data.frame(fit_summary$coefficients)
colnames(coef_out) <- c("estimate", "std_error", "t_value", "p_value")
coef_out$term <- rownames(coef_out)
coef_out <- coef_out[, c("term", "estimate", "std_error", "t_value", "p_value")]
rownames(coef_out) <- NULL
write.csv(coef_out, file.path(out_dir, "lm_coefficients.csv"), row.names = FALSE)

# 回归模型摘要
writeLines(c(
  paste0("线性回归模型: ", lm_y, " ~ ", lm_x),
  paste0("R² = ", r_squared, "    调整后 R² = ", adj_r_squared),
  paste0("回归方程: ", lm_label),
  paste0("整体模型 p 值 (斜率项): ", p_value),
  "",
  capture.output(print(fit_summary))
), file.path(out_dir, "lm_summary.txt"))

# 总体摘要
writeLines(c(
  "========== Athena 生成式界面 Demo 分析摘要 ==========",
  paste0("生成时间: ", format(Sys.time(), "%Y-%m-%d %H:%M:%S")),
  paste0("R 版本:   ", R.version.string),
  "",
  "---- 运行参数 ----",
  paste0("输入文件:     ", ifelse(nzchar(input_file), input_file, "内置 iris 数据集")),
  paste0("聚类数 k:     ", k),
  paste0("随机种子:     ", seed),
  paste0("数据标准化:   ", scale_data),
  paste0("物种筛选:     ", ifelse(nzchar(species_txt), species_txt, "(未筛选)")),
  paste0("调色板:       ", palette_name),
  paste0("点颜色:       ", point_color),
  paste0("点大小/透明度:", point_size, " / ", point_alpha),
  paste0("回归模型:     ", lm_y, " ~ ", lm_x),
  "",
  "---- 数据概况 ----",
  paste0("样本数:       ", nrow(df)),
  paste0("数值变量数:   ", length(num_cols)),
  paste0("变量列表:     ", paste(num_cols, collapse = ", ")),
  "",
  "---- 聚类分析 ----",
  paste0("簇内平方和合计 (tot.withinss): ", round(km$tot.withinss, 4)),
  paste0("组间平方和     (betweenss):   ", round(km$betweenss, 4)),
  paste0("tot.withinss / totalss:       ", round(km$tot.withinss / km$totss, 4)),
  "",
  "---- PCA 降维 ----",
  paste0("前两个主成分累计方差解释比例: ", pc12_var, "%"),
  paste0("各主成分方差解释比例:         ", paste(pca_var$PC, paste0(round(pca_var$variance_ratio * 100, 2), "%"), sep = "=", collapse = "  ")),
  "",
  "---- 线性回归 ----",
  paste0("回归方程: ", lm_label),
  paste0("调整后 R²: ", adj_r_squared, "    斜率项 p 值: ", p_value),
  "",
  "---- 结果文件 ----",
  paste0("图片: ", paste(sort(list.files(out_dir, pattern = "\\.(png|svg|jpg|jpeg)$")), collapse = ", ")),
  paste0("表格: ", paste(sort(list.files(out_dir, pattern = "\\.csv$")), collapse = ", "))
), file.path(out_dir, "analysis_summary.txt"))

cat("\n分析完成！结果文件已经写入:", out_dir, "\n")
cat("  图片:", paste(sort(list.files(out_dir, pattern = "\\.(png|svg|jpg|jpeg)$")), collapse = ", "), "\n")
cat("  表格:", paste(sort(list.files(out_dir, pattern = "\\.csv$")), collapse = ", "), "\n")
