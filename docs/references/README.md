# 参考文献（本地下载）

本目录用于存放方法学参考 PDF。PDF 本身通过 `.gitignore` 排除在仓库之外（避免 25+ MB 二进制入 git），需要时按下面链接重新下载。

## 已用的参考

### UN DESA WPP 2022 Methodology Report

- 文件名：`UN_WPP_2022_Methodology.pdf`
- 下载：https://www.un.org/development/desa/pd/sites/www.un.org.development.desa.pd/files/undesa_pd_2022_wpp_methodology_report.pdf
- 大小：~2.8 MB
- License：CC BY 3.0 IGO
- 用途：本项目 round 3 自查（`docs/AUDIT.md`）以此为对照标准。

### UN Manual X (1983)

- 文件名：`UN_Manual_X_1983.pdf`
- 下载：https://www.un.org/development/desa/pd/sites/www.un.org.development.desa.pd/files/files/documents/2020/Jan/un_1983_manual_x_-_indirect_techniques_for_demographic_estimation.pdf
- 大小：~22 MB
- 用途：未来如做间接估计（reverse-survival, Brass child mortality 等）参考。

## 下载脚本（PowerShell）

```pwsh
$dir = "$PSScriptRoot"
$headers = @{ 'User-Agent' = 'Mozilla/5.0' }
Invoke-WebRequest -Uri "https://www.un.org/development/desa/pd/sites/www.un.org.development.desa.pd/files/undesa_pd_2022_wpp_methodology_report.pdf" `
    -Headers $headers -OutFile "$dir\UN_WPP_2022_Methodology.pdf"
Invoke-WebRequest -Uri "https://www.un.org/development/desa/pd/sites/www.un.org.development.desa.pd/files/files/documents/2020/Jan/un_1983_manual_x_-_indirect_techniques_for_demographic_estimation.pdf" `
    -Headers $headers -OutFile "$dir\UN_Manual_X_1983.pdf"
```

UN 站点对裸 `curl` 返回 403，必须设 User-Agent。
