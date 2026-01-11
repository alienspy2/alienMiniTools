using System.Runtime.InteropServices;

namespace GLBManipulator.Native;

public static unsafe class MeshOptimizer
{
    private const string DllName = "meshoptimizer";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint meshopt_simplify(
        uint* destination,
        uint* indices,
        nuint index_count,
        float* vertex_positions,
        nuint vertex_count,
        nuint vertex_positions_stride,
        nuint target_index_count,
        float target_error,
        uint options,
        float* result_error
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint meshopt_simplifyWithAttributes(
        uint* destination,
        uint* indices,
        nuint index_count,
        float* vertex_positions,
        nuint vertex_count,
        nuint vertex_positions_stride,
        float* vertex_attributes,
        nuint vertex_attributes_stride,
        float* attribute_weights,
        nuint attribute_count,
        uint* vertex_lock,
        nuint target_index_count,
        float target_error,
        uint options,
        float* result_error
    );

    public const uint meshopt_SimplifyLockBorder = 1 << 0;
    public const uint meshopt_SimplifySparse = 1 << 1;
    public const uint meshopt_SimplifyErrorAbsolute = 1 << 2;
}
