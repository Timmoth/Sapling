// GlobalUsings.cs
#if AVX512
global using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx512BW;
global using VectorType = System.Runtime.Intrinsics.Vector512;
global using VectorInt = System.Runtime.Intrinsics.Vector512<int>;
global using VectorShort = System.Runtime.Intrinsics.Vector512<short>;
#else
global using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx2;
global using VectorType = System.Runtime.Intrinsics.Vector256;
global using VectorInt = System.Runtime.Intrinsics.Vector256<int>;
global using VectorShort = System.Runtime.Intrinsics.Vector256<short>;
#endif