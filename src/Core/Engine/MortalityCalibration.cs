namespace ChinaDemographicModel.Core.Engine;

/// **死亡侧口径修正曲线 / Mortality Calibration Curve**
///
/// 对称于 `Calibrator.AlignBirthsToHistory`（出生侧观测锁）的死亡侧实现。
///
/// 背景：q(x) 已由 `CensusLifeTables` 校准到公布 e0，但 CCM 自 1982 逐年推演的
/// 金字塔在**年龄形状**上与普查实测有残差（2020 年 65+ 比七普薄 16.9%，15-64 厚 8.7%），
/// 于是 Σ P(a)·q(a) 系统性低于 NBS 公布死亡数 10-15%。
/// 验证：同一 q(x) 换用七普实测金字塔 → 1040万 vs NBS 998万（+4%）；
///       用模型金字塔 → 870万（−13%）。即缺口来自结构而非死亡率水平。
///
/// 本类记录逐年修正系数 k(y) = 观测死亡 / 模型预测死亡，并对其做
/// **最小二乘线性拟合 + 阻尼外推**，使：
/// - 观测年：模型死亡 ≡ NBS 公布值（硬对齐，同出生侧）；
/// - 预测年：k 由拟合曲线平滑延伸，**不在观测/预测边界产生跳变**（"稳定预期"）；
/// - 外推被阻尼 + 上下限 clamp，避免远期发散。
///
/// 显式命名 + 可查 R²：`RSquared` 报告拟合优度，日志里输出，不藏在私有 helper 里。
public sealed class MortalityCalibration
{
    /// 拟合窗口（末 N 个观测年）。
    public int WindowYears { get; init; } = 12;
    /// 阻尼系数：预测年每远一年，趋势增量按 φ 衰减（φ<1 → 收敛，不发散）。
    public double Damping { get; init; } = 0.85;
    /// k 的合理区间，防止异常数据把修正推到荒谬值。
    public double KFloor { get; init; } = 0.50;
    public double KCeiling { get; init; } = 2.00;

    private readonly List<(int Year, double K)> _points = new();

    private double _slope, _intercept, _rSquared;
    private int _lastYear = int.MinValue;
    private double _lastK = 1.0;
    private bool _fitted;

    /// 观测年算得的 k 入库。
    public void Observe(int year, double k)
    {
        if (double.IsNaN(k) || double.IsInfinity(k) || k <= 0) return;
        _points.Add((year, Math.Clamp(k, KFloor, KCeiling)));
        _lastYear = year;
        _lastK = Math.Clamp(k, KFloor, KCeiling);
        _fitted = false;
    }

    /// 拟合优度 R²（末窗口内）。点数不足返回 0。
    public double RSquared { get { EnsureFit(); return _rSquared; } }

    public int PointCount => _points.Count;

    /// 预测年的修正系数：拟合直线 + 阻尼外推 + clamp。
    /// 边界连续性：h=0 时返回末观测 k 本身，故 观测→预测 不跳变。
    public double ProjectK(int year)
    {
        EnsureFit();
        if (_points.Count == 0) return 1.0;
        if (_points.Count < 2) return _lastK;

        int h = year - _lastYear;
        if (h <= 0) return _lastK;

        // 阻尼几何和：kLast + slope * Σ_{i=1..h} φ^(i-1)
        double geo = Math.Abs(1 - Damping) < 1e-9 ? h : (1 - Math.Pow(Damping, h)) / (1 - Damping);
        double k = _lastK + _slope * geo;
        return Math.Clamp(k, KFloor, KCeiling);
    }

    private void EnsureFit()
    {
        if (_fitted) return;
        _fitted = true;
        _slope = 0; _intercept = _lastK; _rSquared = 0;

        var win = _points.Count <= WindowYears
            ? _points
            : _points.GetRange(_points.Count - WindowYears, WindowYears);
        if (win.Count < 2) return;

        double xBar = win.Average(p => (double)p.Year);
        double yBar = win.Average(p => p.K);
        double num = 0, den = 0;
        foreach (var p in win)
        {
            double dx = p.Year - xBar;
            num += dx * (p.K - yBar);
            den += dx * dx;
        }
        _slope = den > 0 ? num / den : 0;
        _intercept = yBar - _slope * xBar;

        double ssTot = 0, ssRes = 0;
        foreach (var p in win)
        {
            double pred = _intercept + _slope * p.Year;
            ssTot += (p.K - yBar) * (p.K - yBar);
            ssRes += (p.K - pred) * (p.K - pred);
        }
        _rSquared = ssTot > 1e-12 ? Math.Max(0, 1 - ssRes / ssTot) : 1.0;
    }
}
