/// <summary>
/// HarmonicCellGenerator — Option L world-gen: builds a 2-D heightmap and
/// biome-cell grid by sampling the live GR and Magic field operators.
///
/// Torsion semantics (NEM §world-gen):
///   Low  |TotalTorsion| → depth near 1.0 → full curvature + high p-Laplacian
///     → large height variance, many Anomaly and extreme cells.
///   High |TotalTorsion| → depth near 0.1 → suppressed curvature + p→2 (linear)
///     → flat terrain, fewer anomalies — the pocket-world is "safe".
///
/// Usage:
///   var gen = new HarmonicCellGenerator(grOp, magicOp, ham);
///   gen.Generate();
///   // read gen.Heightmap[x, z] and gen.Cells[x, z]
///
/// The resulting Heightmap / Cells arrays are the source data for a TileMap or
/// MeshInstance.  Baalzemon and Bumble can be placed in/near Anomaly or Plain
/// regions by scanning Cells after generation.
/// </summary>
public class HarmonicCellGenerator
{
    private readonly GR_Operator         _gr;
    private readonly MagicFieldOperator  _magic;
    private readonly HarmonicAttentionMatrix _ham;

    public int SizeX = 512;
    public int SizeZ = 512;

    /// <summary>
    /// Normalised height values ∈ [0, 1] indexed [x, z].
    /// Populated after Generate() is called.
    /// </summary>
    public float[,]    Heightmap = null!;

    /// <summary>
    /// Biome cell type indexed [x, z].
    /// Populated after Generate() is called.
    /// </summary>
    public CellType[,] Cells     = null!;

    // ── seeding parameters ────────────────────────────────────────────────────

    /// <summary>
    /// Frequency of the procedural base curvature pattern.
    /// Higher → finer terrain detail.  Default 0.05.
    /// </summary>
    public float CurvatureFrequency = 0.05f;

    /// <summary>
    /// Frequency of the procedural magic flux pattern.
    /// Higher → denser anomaly scatter.  Default 0.07.
    /// </summary>
    public float MagicFluxFrequency = 0.07f;

    public HarmonicCellGenerator(
        GR_Operator grOp,
        MagicFieldOperator magicOp,
        HarmonicAttentionMatrix hamRef)
    {
        _gr    = grOp;
        _magic = magicOp;
        _ham   = hamRef;

        Heightmap = new float[SizeX, SizeZ];
        Cells     = new CellType[SizeX, SizeZ];
    }

    /// <summary>
    /// Populate Heightmap and Cells.
    ///
    /// For each (x, z):
    ///   1. Derive a base curvature scalar from a deterministic pattern.
    ///   2. Pass through GR_Operator (depth-scaled curvature → harmonic smooth).
    ///   3. Derive a base magic flux scalar from a separate pattern.
    ///   4. Pass through MagicFieldOperator (depth-scaled → p-Laplacian nonlinearity).
    ///   5. Resolve CellType from (height, magicVal, totalTorsion).
    /// </summary>
    public void Generate()
    {
        // Snapshot torsion once — it is a manifold-level scalar, not per-cell.
        float torsion = _ham.TotalTorsion();

        for (int x = 0; x < SizeX; x++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                // ── GR curvature path ────────────────────────────────────────
                // Base curvature from a simple sine-product pattern.
                // Replace with Perlin / simplex noise at integration time.
                float baseCurv = 0.5f
                    + 0.5f * SinApprox(x * CurvatureFrequency)
                           * SinApprox(z * CurvatureFrequency);

                float h = _gr.Apply(baseCurv);   // depth-scale + harmonic smooth
                Heightmap[x, z] = h;

                // ── Magic flux path ──────────────────────────────────────────
                float baseFlux = 0.5f
                    + 0.5f * SinApprox((x + z * 0.37f) * MagicFluxFrequency);

                float magicVal = _magic.Apply(baseFlux); // depth-scale + p-Laplacian

                // ── Biome resolution ─────────────────────────────────────────
                Cells[x, z] = ResolveCellType(h, magicVal, torsion);
            }
        }
    }

    /// <summary>
    /// Map (height, magic value, total torsion) to a CellType.
    ///
    /// Torsion amplitude shifts the anomaly and extreme-terrain thresholds:
    ///   Low  |torsion| (tShift → 0)   → thresholds are tightest → more extremes.
    ///   High |torsion| (tShift large)  → thresholds relax → flatter, safer world.
    ///
    /// Priority: Anomaly > DeepPit > HighRidge > Plain.
    /// </summary>
    private static CellType ResolveCellType(float h, float magicVal, float torsion)
    {
        float tShift = System.Math.Abs(torsion) * 0.01f;

        if (magicVal > 0.7f + tShift) return CellType.Anomaly;
        if (h        < 0.2f - tShift) return CellType.DeepPit;
        if (h        > 0.8f + tShift) return CellType.HighRidge;

        return CellType.Plain;
    }

    // Fast deterministic sine approximation (no System.Math dependency).
    // Returns a value in [−1, 1].
    private static float SinApprox(float x)
    {
        // Normalise to [0, 2π] period via Godot.Mathf
        return Godot.Mathf.Sin(x);
    }
}

/// <summary>
/// Biome cell types produced by HarmonicCellGenerator.
/// </summary>
public enum CellType
{
    /// <summary>Baseline terrain — no strong height or magic signature.</summary>
    Plain,
    /// <summary>High-curvature ridge — h > upper threshold.</summary>
    HighRidge,
    /// <summary>Deep concavity — h &lt; lower threshold.</summary>
    DeepPit,
    /// <summary>High magic flux — candidate spawn point for Baalzemon.</summary>
    Anomaly,
}
