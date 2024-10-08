
using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private static readonly VectorShort Ceil = VectorType.Create<short>(255);
    private static readonly VectorShort Floor = VectorType.Create<short>(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdResetAccumulators(VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        *(whiteAcc + 0) = *(blackAcc + 0) = *(NnueWeights.FeatureBiases + 0);
        *(whiteAcc + 1) = *(blackAcc + 1) = *(NnueWeights.FeatureBiases + 1);
        *(whiteAcc + 2) = *(blackAcc + 2) = *(NnueWeights.FeatureBiases + 2);
        *(whiteAcc + 3) = *(blackAcc + 3) = *(NnueWeights.FeatureBiases + 3);
        *(whiteAcc + 4) = *(blackAcc + 4) = *(NnueWeights.FeatureBiases + 4);
        *(whiteAcc + 5) = *(blackAcc + 5) = *(NnueWeights.FeatureBiases + 5);
        *(whiteAcc + 6) = *(blackAcc + 6) = *(NnueWeights.FeatureBiases + 6);
        *(whiteAcc + 7) = *(blackAcc + 7) = *(NnueWeights.FeatureBiases + 7);
        *(whiteAcc + 8) = *(blackAcc + 8) = *(NnueWeights.FeatureBiases + 8);
        *(whiteAcc + 9) = *(blackAcc + 9) = *(NnueWeights.FeatureBiases + 9);
        *(whiteAcc + 10) = *(blackAcc + 10) = *(NnueWeights.FeatureBiases + 10);
        *(whiteAcc + 11) = *(blackAcc + 11) = *(NnueWeights.FeatureBiases + 11);
        *(whiteAcc + 12) = *(blackAcc + 12) = *(NnueWeights.FeatureBiases + 12);
        *(whiteAcc + 13) = *(blackAcc + 13) = *(NnueWeights.FeatureBiases + 13);
        *(whiteAcc + 14) = *(blackAcc + 14) = *(NnueWeights.FeatureBiases + 14);
        *(whiteAcc + 15) = *(blackAcc + 15) = *(NnueWeights.FeatureBiases + 15);
        *(whiteAcc + 16) = *(blackAcc + 16) = *(NnueWeights.FeatureBiases + 16);
        *(whiteAcc + 17) = *(blackAcc + 17) = *(NnueWeights.FeatureBiases + 17);
        *(whiteAcc + 18) = *(blackAcc + 18) = *(NnueWeights.FeatureBiases + 18);
        *(whiteAcc + 19) = *(blackAcc + 19) = *(NnueWeights.FeatureBiases + 19);
        *(whiteAcc + 20) = *(blackAcc + 20) = *(NnueWeights.FeatureBiases + 20);
        *(whiteAcc + 21) = *(blackAcc + 21) = *(NnueWeights.FeatureBiases + 21);
        *(whiteAcc + 22) = *(blackAcc + 22) = *(NnueWeights.FeatureBiases + 22);
        *(whiteAcc + 23) = *(blackAcc + 23) = *(NnueWeights.FeatureBiases + 23);
        *(whiteAcc + 24) = *(blackAcc + 24) = *(NnueWeights.FeatureBiases + 24);
        *(whiteAcc + 25) = *(blackAcc + 25) = *(NnueWeights.FeatureBiases + 25);
        *(whiteAcc + 26) = *(blackAcc + 26) = *(NnueWeights.FeatureBiases + 26);
        *(whiteAcc + 27) = *(blackAcc + 27) = *(NnueWeights.FeatureBiases + 27);
        *(whiteAcc + 28) = *(blackAcc + 28) = *(NnueWeights.FeatureBiases + 28);
        *(whiteAcc + 29) = *(blackAcc + 29) = *(NnueWeights.FeatureBiases + 29);
        *(whiteAcc + 30) = *(blackAcc + 30) = *(NnueWeights.FeatureBiases + 30);
        *(whiteAcc + 31) = *(blackAcc + 31) = *(NnueWeights.FeatureBiases + 31);
#if !AVX512
        *(whiteAcc + 32) = *(blackAcc + 32) = *(NnueWeights.FeatureBiases + 32);
        *(whiteAcc + 33) = *(blackAcc + 33) = *(NnueWeights.FeatureBiases + 33);
        *(whiteAcc + 34) = *(blackAcc + 34) = *(NnueWeights.FeatureBiases + 34);
        *(whiteAcc + 35) = *(blackAcc + 35) = *(NnueWeights.FeatureBiases + 35);
        *(whiteAcc + 36) = *(blackAcc + 36) = *(NnueWeights.FeatureBiases + 36);
        *(whiteAcc + 37) = *(blackAcc + 37) = *(NnueWeights.FeatureBiases + 37);
        *(whiteAcc + 38) = *(blackAcc + 38) = *(NnueWeights.FeatureBiases + 38);
        *(whiteAcc + 39) = *(blackAcc + 39) = *(NnueWeights.FeatureBiases + 39);
        *(whiteAcc + 40) = *(blackAcc + 40) = *(NnueWeights.FeatureBiases + 40);
        *(whiteAcc + 41) = *(blackAcc + 41) = *(NnueWeights.FeatureBiases + 41);
        *(whiteAcc + 42) = *(blackAcc + 42) = *(NnueWeights.FeatureBiases + 42);
        *(whiteAcc + 43) = *(blackAcc + 43) = *(NnueWeights.FeatureBiases + 43);
        *(whiteAcc + 44) = *(blackAcc + 44) = *(NnueWeights.FeatureBiases + 44);
        *(whiteAcc + 45) = *(blackAcc + 45) = *(NnueWeights.FeatureBiases + 45);
        *(whiteAcc + 46) = *(blackAcc + 46) = *(NnueWeights.FeatureBiases + 46);
        *(whiteAcc + 47) = *(blackAcc + 47) = *(NnueWeights.FeatureBiases + 47);
        *(whiteAcc + 48) = *(blackAcc + 48) = *(NnueWeights.FeatureBiases + 48);
        *(whiteAcc + 49) = *(blackAcc + 49) = *(NnueWeights.FeatureBiases + 49);
        *(whiteAcc + 50) = *(blackAcc + 50) = *(NnueWeights.FeatureBiases + 50);
        *(whiteAcc + 51) = *(blackAcc + 51) = *(NnueWeights.FeatureBiases + 51);
        *(whiteAcc + 52) = *(blackAcc + 52) = *(NnueWeights.FeatureBiases + 52);
        *(whiteAcc + 53) = *(blackAcc + 53) = *(NnueWeights.FeatureBiases + 53);
        *(whiteAcc + 54) = *(blackAcc + 54) = *(NnueWeights.FeatureBiases + 54);
        *(whiteAcc + 55) = *(blackAcc + 55) = *(NnueWeights.FeatureBiases + 55);
        *(whiteAcc + 56) = *(blackAcc + 56) = *(NnueWeights.FeatureBiases + 56);
        *(whiteAcc + 57) = *(blackAcc + 57) = *(NnueWeights.FeatureBiases + 57);
        *(whiteAcc + 58) = *(blackAcc + 58) = *(NnueWeights.FeatureBiases + 58);
        *(whiteAcc + 59) = *(blackAcc + 59) = *(NnueWeights.FeatureBiases + 59);
        *(whiteAcc + 60) = *(blackAcc + 60) = *(NnueWeights.FeatureBiases + 60);
        *(whiteAcc + 61) = *(blackAcc + 61) = *(NnueWeights.FeatureBiases + 61);
        *(whiteAcc + 62) = *(blackAcc + 62) = *(NnueWeights.FeatureBiases + 62);
        *(whiteAcc + 63) = *(blackAcc + 63) = *(NnueWeights.FeatureBiases + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdResetAccumulator(VectorShort* acc)
    {
        *(acc + 0) = *(NnueWeights.FeatureBiases + 0);
        *(acc + 1) = *(NnueWeights.FeatureBiases + 1);
        *(acc + 2) = *(NnueWeights.FeatureBiases + 2);
        *(acc + 3) = *(NnueWeights.FeatureBiases + 3);
        *(acc + 4) = *(NnueWeights.FeatureBiases + 4);
        *(acc + 5) = *(NnueWeights.FeatureBiases + 5);
        *(acc + 6) = *(NnueWeights.FeatureBiases + 6);
        *(acc + 7) = *(NnueWeights.FeatureBiases + 7);
        *(acc + 8) = *(NnueWeights.FeatureBiases + 8);
        *(acc + 9) = *(NnueWeights.FeatureBiases + 9);
        *(acc + 10) = *(NnueWeights.FeatureBiases + 10);
        *(acc + 11) = *(NnueWeights.FeatureBiases + 11);
        *(acc + 12) = *(NnueWeights.FeatureBiases + 12);
        *(acc + 13) = *(NnueWeights.FeatureBiases + 13);
        *(acc + 14) = *(NnueWeights.FeatureBiases + 14);
        *(acc + 15) = *(NnueWeights.FeatureBiases + 15);
        *(acc + 16) = *(NnueWeights.FeatureBiases + 16);
        *(acc + 17) = *(NnueWeights.FeatureBiases + 17);
        *(acc + 18) = *(NnueWeights.FeatureBiases + 18);
        *(acc + 19) = *(NnueWeights.FeatureBiases + 19);
        *(acc + 20) = *(NnueWeights.FeatureBiases + 20);
        *(acc + 21) = *(NnueWeights.FeatureBiases + 21);
        *(acc + 22) = *(NnueWeights.FeatureBiases + 22);
        *(acc + 23) = *(NnueWeights.FeatureBiases + 23);
        *(acc + 24) = *(NnueWeights.FeatureBiases + 24);
        *(acc + 25) = *(NnueWeights.FeatureBiases + 25);
        *(acc + 26) = *(NnueWeights.FeatureBiases + 26);
        *(acc + 27) = *(NnueWeights.FeatureBiases + 27);
        *(acc + 28) = *(NnueWeights.FeatureBiases + 28);
        *(acc + 29) = *(NnueWeights.FeatureBiases + 29);
        *(acc + 30) = *(NnueWeights.FeatureBiases + 30);
        *(acc + 31) = *(NnueWeights.FeatureBiases + 31);
#if !AVX512
        *(acc + 32) = *(NnueWeights.FeatureBiases + 32);
        *(acc + 33) = *(NnueWeights.FeatureBiases + 33);
        *(acc + 34) = *(NnueWeights.FeatureBiases + 34);
        *(acc + 35) = *(NnueWeights.FeatureBiases + 35);
        *(acc + 36) = *(NnueWeights.FeatureBiases + 36);
        *(acc + 37) = *(NnueWeights.FeatureBiases + 37);
        *(acc + 38) = *(NnueWeights.FeatureBiases + 38);
        *(acc + 39) = *(NnueWeights.FeatureBiases + 39);
        *(acc + 40) = *(NnueWeights.FeatureBiases + 40);
        *(acc + 41) = *(NnueWeights.FeatureBiases + 41);
        *(acc + 42) = *(NnueWeights.FeatureBiases + 42);
        *(acc + 43) = *(NnueWeights.FeatureBiases + 43);
        *(acc + 44) = *(NnueWeights.FeatureBiases + 44);
        *(acc + 45) = *(NnueWeights.FeatureBiases + 45);
        *(acc + 46) = *(NnueWeights.FeatureBiases + 46);
        *(acc + 47) = *(NnueWeights.FeatureBiases + 47);
        *(acc + 48) = *(NnueWeights.FeatureBiases + 48);
        *(acc + 49) = *(NnueWeights.FeatureBiases + 49);
        *(acc + 50) = *(NnueWeights.FeatureBiases + 50);
        *(acc + 51) = *(NnueWeights.FeatureBiases + 51);
        *(acc + 52) = *(NnueWeights.FeatureBiases + 52);
        *(acc + 53) = *(NnueWeights.FeatureBiases + 53);
        *(acc + 54) = *(NnueWeights.FeatureBiases + 54);
        *(acc + 55) = *(NnueWeights.FeatureBiases + 55);
        *(acc + 56) = *(NnueWeights.FeatureBiases + 56);
        *(acc + 57) = *(NnueWeights.FeatureBiases + 57);
        *(acc + 58) = *(NnueWeights.FeatureBiases + 58);
        *(acc + 59) = *(NnueWeights.FeatureBiases + 59);
        *(acc + 60) = *(NnueWeights.FeatureBiases + 60);
        *(acc + 61) = *(NnueWeights.FeatureBiases + 61);
        *(acc + 62) = *(NnueWeights.FeatureBiases + 62);
        *(acc + 63) = *(NnueWeights.FeatureBiases + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdCopy(VectorShort* dest, VectorShort* src)
    {
        *(dest + 0) = *(src + 0);
        *(dest + 1) = *(src + 1);
        *(dest + 2) = *(src + 2);
        *(dest + 3) = *(src + 3);
        *(dest + 4) = *(src + 4);
        *(dest + 5) = *(src + 5);
        *(dest + 6) = *(src + 6);
        *(dest + 7) = *(src + 7);
        *(dest + 8) = *(src + 8);
        *(dest + 9) = *(src + 9);
        *(dest + 10) = *(src + 10);
        *(dest + 11) = *(src + 11);
        *(dest + 12) = *(src + 12);
        *(dest + 13) = *(src + 13);
        *(dest + 14) = *(src + 14);
        *(dest + 15) = *(src + 15);
        *(dest + 16) = *(src + 16);
        *(dest + 17) = *(src + 17);
        *(dest + 18) = *(src + 18);
        *(dest + 19) = *(src + 19);
        *(dest + 20) = *(src + 20);
        *(dest + 21) = *(src + 21);
        *(dest + 22) = *(src + 22);
        *(dest + 23) = *(src + 23);
        *(dest + 24) = *(src + 24);
        *(dest + 25) = *(src + 25);
        *(dest + 26) = *(src + 26);
        *(dest + 27) = *(src + 27);
        *(dest + 28) = *(src + 28);
        *(dest + 29) = *(src + 29);
        *(dest + 30) = *(src + 30);
        *(dest + 31) = *(src + 31);
#if !AVX512
        *(dest + 32) = *(src + 32);
        *(dest + 33) = *(src + 33);
        *(dest + 34) = *(src + 34);
        *(dest + 35) = *(src + 35);
        *(dest + 36) = *(src + 36);
        *(dest + 37) = *(src + 37);
        *(dest + 38) = *(src + 38);
        *(dest + 39) = *(src + 39);
        *(dest + 40) = *(src + 40);
        *(dest + 41) = *(src + 41);
        *(dest + 42) = *(src + 42);
        *(dest + 43) = *(src + 43);
        *(dest + 44) = *(src + 44);
        *(dest + 45) = *(src + 45);
        *(dest + 46) = *(src + 46);
        *(dest + 47) = *(src + 47);
        *(dest + 48) = *(src + 48);
        *(dest + 49) = *(src + 49);
        *(dest + 50) = *(src + 50);
        *(dest + 51) = *(src + 51);
        *(dest + 52) = *(src + 52);
        *(dest + 53) = *(src + 53);
        *(dest + 54) = *(src + 54);
        *(dest + 55) = *(src + 55);
        *(dest + 56) = *(src + 56);
        *(dest + 57) = *(src + 57);
        *(dest + 58) = *(src + 58);
        *(dest + 59) = *(src + 59);
        *(dest + 60) = *(src + 60);
        *(dest + 61) = *(src + 61);
        *(dest + 62) = *(src + 62);
        *(dest + 63) = *(src + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sub(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub)
    {
        *(dest + 0) = *(source + 0) - *(sub + 0);
        *(dest + 1) = *(source + 1) - *(sub + 1);
        *(dest + 2) = *(source + 2) - *(sub + 2);
        *(dest + 3) = *(source + 3) - *(sub + 3);
        *(dest + 4) = *(source + 4) - *(sub + 4);
        *(dest + 5) = *(source + 5) - *(sub + 5);
        *(dest + 6) = *(source + 6) - *(sub + 6);
        *(dest + 7) = *(source + 7) - *(sub + 7);
        *(dest + 8) = *(source + 8) - *(sub + 8);
        *(dest + 9) = *(source + 9) - *(sub + 9);
        *(dest + 10) = *(source + 10) - *(sub + 10);
        *(dest + 11) = *(source + 11) - *(sub + 11);
        *(dest + 12) = *(source + 12) - *(sub + 12);
        *(dest + 13) = *(source + 13) - *(sub + 13);
        *(dest + 14) = *(source + 14) - *(sub + 14);
        *(dest + 15) = *(source + 15) - *(sub + 15);
        *(dest + 16) = *(source + 16) - *(sub + 16);
        *(dest + 17) = *(source + 17) - *(sub + 17);
        *(dest + 18) = *(source + 18) - *(sub + 18);
        *(dest + 19) = *(source + 19) - *(sub + 19);
        *(dest + 20) = *(source + 20) - *(sub + 20);
        *(dest + 21) = *(source + 21) - *(sub + 21);
        *(dest + 22) = *(source + 22) - *(sub + 22);
        *(dest + 23) = *(source + 23) - *(sub + 23);
        *(dest + 24) = *(source + 24) - *(sub + 24);
        *(dest + 25) = *(source + 25) - *(sub + 25);
        *(dest + 26) = *(source + 26) - *(sub + 26);
        *(dest + 27) = *(source + 27) - *(sub + 27);
        *(dest + 28) = *(source + 28) - *(sub + 28);
        *(dest + 29) = *(source + 29) - *(sub + 29);
        *(dest + 30) = *(source + 30) - *(sub + 30);
        *(dest + 31) = *(source + 31) - *(sub + 31);
#if !AVX512
        *(dest + 32) = *(source + 32) - *(sub + 32);
        *(dest + 33) = *(source + 33) - *(sub + 33);
        *(dest + 34) = *(source + 34) - *(sub + 34);
        *(dest + 35) = *(source + 35) - *(sub + 35);
        *(dest + 36) = *(source + 36) - *(sub + 36);
        *(dest + 37) = *(source + 37) - *(sub + 37);
        *(dest + 38) = *(source + 38) - *(sub + 38);
        *(dest + 39) = *(source + 39) - *(sub + 39);
        *(dest + 40) = *(source + 40) - *(sub + 40);
        *(dest + 41) = *(source + 41) - *(sub + 41);
        *(dest + 42) = *(source + 42) - *(sub + 42);
        *(dest + 43) = *(source + 43) - *(sub + 43);
        *(dest + 44) = *(source + 44) - *(sub + 44);
        *(dest + 45) = *(source + 45) - *(sub + 45);
        *(dest + 46) = *(source + 46) - *(sub + 46);
        *(dest + 47) = *(source + 47) - *(sub + 47);
        *(dest + 48) = *(source + 48) - *(sub + 48);
        *(dest + 49) = *(source + 49) - *(sub + 49);
        *(dest + 50) = *(source + 50) - *(sub + 50);
        *(dest + 51) = *(source + 51) - *(sub + 51);
        *(dest + 52) = *(source + 52) - *(sub + 52);
        *(dest + 53) = *(source + 53) - *(sub + 53);
        *(dest + 54) = *(source + 54) - *(sub + 54);
        *(dest + 55) = *(source + 55) - *(sub + 55);
        *(dest + 56) = *(source + 56) - *(sub + 56);
        *(dest + 57) = *(source + 57) - *(sub + 57);
        *(dest + 58) = *(source + 58) - *(sub + 58);
        *(dest + 59) = *(source + 59) - *(sub + 59);
        *(dest + 60) = *(source + 60) - *(sub + 60);
        *(dest + 61) = *(source + 61) - *(sub + 61);
        *(dest + 62) = *(source + 62) - *(sub + 62);
        *(dest + 63) = *(source + 63) - *(sub + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* add)
    {
        *(dest + 0) = *(source + 0) + *(add + 0);
        *(dest + 1) = *(source + 1) + *(add + 1);
        *(dest + 2) = *(source + 2) + *(add + 2);
        *(dest + 3) = *(source + 3) + *(add + 3);
        *(dest + 4) = *(source + 4) + *(add + 4);
        *(dest + 5) = *(source + 5) + *(add + 5);
        *(dest + 6) = *(source + 6) + *(add + 6);
        *(dest + 7) = *(source + 7) + *(add + 7);
        *(dest + 8) = *(source + 8) + *(add + 8);
        *(dest + 9) = *(source + 9) + *(add + 9);
        *(dest + 10) = *(source + 10) + *(add + 10);
        *(dest + 11) = *(source + 11) + *(add + 11);
        *(dest + 12) = *(source + 12) + *(add + 12);
        *(dest + 13) = *(source + 13) + *(add + 13);
        *(dest + 14) = *(source + 14) + *(add + 14);
        *(dest + 15) = *(source + 15) + *(add + 15);
        *(dest + 16) = *(source + 16) + *(add + 16);
        *(dest + 17) = *(source + 17) + *(add + 17);
        *(dest + 18) = *(source + 18) + *(add + 18);
        *(dest + 19) = *(source + 19) + *(add + 19);
        *(dest + 20) = *(source + 20) + *(add + 20);
        *(dest + 21) = *(source + 21) + *(add + 21);
        *(dest + 22) = *(source + 22) + *(add + 22);
        *(dest + 23) = *(source + 23) + *(add + 23);
        *(dest + 24) = *(source + 24) + *(add + 24);
        *(dest + 25) = *(source + 25) + *(add + 25);
        *(dest + 26) = *(source + 26) + *(add + 26);
        *(dest + 27) = *(source + 27) + *(add + 27);
        *(dest + 28) = *(source + 28) + *(add + 28);
        *(dest + 29) = *(source + 29) + *(add + 29);
        *(dest + 30) = *(source + 30) + *(add + 30);
        *(dest + 31) = *(source + 31) + *(add + 31);
#if !AVX512
        *(dest + 32) = *(source + 32) + *(add + 32);
        *(dest + 33) = *(source + 33) + *(add + 33);
        *(dest + 34) = *(source + 34) + *(add + 34);
        *(dest + 35) = *(source + 35) + *(add + 35);
        *(dest + 36) = *(source + 36) + *(add + 36);
        *(dest + 37) = *(source + 37) + *(add + 37);
        *(dest + 38) = *(source + 38) + *(add + 38);
        *(dest + 39) = *(source + 39) + *(add + 39);
        *(dest + 40) = *(source + 40) + *(add + 40);
        *(dest + 41) = *(source + 41) + *(add + 41);
        *(dest + 42) = *(source + 42) + *(add + 42);
        *(dest + 43) = *(source + 43) + *(add + 43);
        *(dest + 44) = *(source + 44) + *(add + 44);
        *(dest + 45) = *(source + 45) + *(add + 45);
        *(dest + 46) = *(source + 46) + *(add + 46);
        *(dest + 47) = *(source + 47) + *(add + 47);
        *(dest + 48) = *(source + 48) + *(add + 48);
        *(dest + 49) = *(source + 49) + *(add + 49);
        *(dest + 50) = *(source + 50) + *(add + 50);
        *(dest + 51) = *(source + 51) + *(add + 51);
        *(dest + 52) = *(source + 52) + *(add + 52);
        *(dest + 53) = *(source + 53) + *(add + 53);
        *(dest + 54) = *(source + 54) + *(add + 54);
        *(dest + 55) = *(source + 55) + *(add + 55);
        *(dest + 56) = *(source + 56) + *(add + 56);
        *(dest + 57) = *(source + 57) + *(add + 57);
        *(dest + 58) = *(source + 58) + *(add + 58);
        *(dest + 59) = *(source + 59) + *(add + 59);
        *(dest + 60) = *(source + 60) + *(add + 60);
        *(dest + 61) = *(source + 61) + *(add + 61);
        *(dest + 62) = *(source + 62) + *(add + 62);
        *(dest + 63) = *(source + 63) + *(add + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddWeights(VectorShort* accuPtr, VectorShort* featurePtr)
    {
        *(accuPtr + 0) += *(featurePtr + 0);
        *(accuPtr + 1) += *(featurePtr + 1);
        *(accuPtr + 2) += *(featurePtr + 2);
        *(accuPtr + 3) += *(featurePtr + 3);
        *(accuPtr + 4) += *(featurePtr + 4);
        *(accuPtr + 5) += *(featurePtr + 5);
        *(accuPtr + 6) += *(featurePtr + 6);
        *(accuPtr + 7) += *(featurePtr + 7);
        *(accuPtr + 8) += *(featurePtr + 8);
        *(accuPtr + 9) += *(featurePtr + 9);
        *(accuPtr + 10) += *(featurePtr + 10);
        *(accuPtr + 11) += *(featurePtr + 11);
        *(accuPtr + 12) += *(featurePtr + 12);
        *(accuPtr + 13) += *(featurePtr + 13);
        *(accuPtr + 14) += *(featurePtr + 14);
        *(accuPtr + 15) += *(featurePtr + 15);
        *(accuPtr + 16) += *(featurePtr + 16);
        *(accuPtr + 17) += *(featurePtr + 17);
        *(accuPtr + 18) += *(featurePtr + 18);
        *(accuPtr + 19) += *(featurePtr + 19);
        *(accuPtr + 20) += *(featurePtr + 20);
        *(accuPtr + 21) += *(featurePtr + 21);
        *(accuPtr + 22) += *(featurePtr + 22);
        *(accuPtr + 23) += *(featurePtr + 23);
        *(accuPtr + 24) += *(featurePtr + 24);
        *(accuPtr + 25) += *(featurePtr + 25);
        *(accuPtr + 26) += *(featurePtr + 26);
        *(accuPtr + 27) += *(featurePtr + 27);
        *(accuPtr + 28) += *(featurePtr + 28);
        *(accuPtr + 29) += *(featurePtr + 29);
        *(accuPtr + 30) += *(featurePtr + 30);
        *(accuPtr + 31) += *(featurePtr + 31);
#if !AVX512
        *(accuPtr + 32) += *(featurePtr + 32);
        *(accuPtr + 33) += *(featurePtr + 33);
        *(accuPtr + 34) += *(featurePtr + 34);
        *(accuPtr + 35) += *(featurePtr + 35);
        *(accuPtr + 36) += *(featurePtr + 36);
        *(accuPtr + 37) += *(featurePtr + 37);
        *(accuPtr + 38) += *(featurePtr + 38);
        *(accuPtr + 39) += *(featurePtr + 39);
        *(accuPtr + 40) += *(featurePtr + 40);
        *(accuPtr + 41) += *(featurePtr + 41);
        *(accuPtr + 42) += *(featurePtr + 42);
        *(accuPtr + 43) += *(featurePtr + 43);
        *(accuPtr + 44) += *(featurePtr + 44);
        *(accuPtr + 45) += *(featurePtr + 45);
        *(accuPtr + 46) += *(featurePtr + 46);
        *(accuPtr + 47) += *(featurePtr + 47);
        *(accuPtr + 48) += *(featurePtr + 48);
        *(accuPtr + 49) += *(featurePtr + 49);
        *(accuPtr + 50) += *(featurePtr + 50);
        *(accuPtr + 51) += *(featurePtr + 51);
        *(accuPtr + 52) += *(featurePtr + 52);
        *(accuPtr + 53) += *(featurePtr + 53);
        *(accuPtr + 54) += *(featurePtr + 54);
        *(accuPtr + 55) += *(featurePtr + 55);
        *(accuPtr + 56) += *(featurePtr + 56);
        *(accuPtr + 57) += *(featurePtr + 57);
        *(accuPtr + 58) += *(featurePtr + 58);
        *(accuPtr + 59) += *(featurePtr + 59);
        *(accuPtr + 60) += *(featurePtr + 60);
        *(accuPtr + 61) += *(featurePtr + 61);
        *(accuPtr + 62) += *(featurePtr + 62);
        *(accuPtr + 63) += *(featurePtr + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ForwardCReLU(VectorShort* usAcc, VectorShort* themAcc, int bucket)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights + bucket * AccumulatorSize * 2;
        var themWeightsPtr = featureWeightsPtr + AccumulatorSize;

        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 0), Ceil), Floor), *(featureWeightsPtr + 0)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 0), Ceil), Floor), *(themWeightsPtr + 0));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 1), Ceil), Floor), *(featureWeightsPtr + 1)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 1), Ceil), Floor), *(themWeightsPtr + 1));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 2), Ceil), Floor), *(featureWeightsPtr + 2)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 2), Ceil), Floor), *(themWeightsPtr + 2));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 3), Ceil), Floor), *(featureWeightsPtr + 3)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 3), Ceil), Floor), *(themWeightsPtr + 3));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 4), Ceil), Floor), *(featureWeightsPtr + 4)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 4), Ceil), Floor), *(themWeightsPtr + 4));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 5), Ceil), Floor), *(featureWeightsPtr + 5)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 5), Ceil), Floor), *(themWeightsPtr + 5));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 6), Ceil), Floor), *(featureWeightsPtr + 6)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 6), Ceil), Floor), *(themWeightsPtr + 6));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 7), Ceil), Floor), *(featureWeightsPtr + 7)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 7), Ceil), Floor), *(themWeightsPtr + 7));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 8), Ceil), Floor), *(featureWeightsPtr + 8)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 8), Ceil), Floor), *(themWeightsPtr + 8));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 9), Ceil), Floor), *(featureWeightsPtr + 9)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 9), Ceil), Floor), *(themWeightsPtr + 9));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 10), Ceil), Floor), *(featureWeightsPtr + 10)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 10), Ceil), Floor), *(themWeightsPtr + 10));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 11), Ceil), Floor), *(featureWeightsPtr + 11)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 11), Ceil), Floor), *(themWeightsPtr + 11));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 12), Ceil), Floor), *(featureWeightsPtr + 12)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 12), Ceil), Floor), *(themWeightsPtr + 12));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 13), Ceil), Floor), *(featureWeightsPtr + 13)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 13), Ceil), Floor), *(themWeightsPtr + 13));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 14), Ceil), Floor), *(featureWeightsPtr + 14)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 14), Ceil), Floor), *(themWeightsPtr + 14));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 15), Ceil), Floor), *(featureWeightsPtr + 15)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 15), Ceil), Floor), *(themWeightsPtr + 15));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 16), Ceil), Floor), *(featureWeightsPtr + 16)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 16), Ceil), Floor), *(themWeightsPtr + 16));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 17), Ceil), Floor), *(featureWeightsPtr + 17)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 17), Ceil), Floor), *(themWeightsPtr + 17));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 18), Ceil), Floor), *(featureWeightsPtr + 18)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 18), Ceil), Floor), *(themWeightsPtr + 18));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 19), Ceil), Floor), *(featureWeightsPtr + 19)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 19), Ceil), Floor), *(themWeightsPtr + 19));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 20), Ceil), Floor), *(featureWeightsPtr + 20)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 20), Ceil), Floor), *(themWeightsPtr + 20));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 21), Ceil), Floor), *(featureWeightsPtr + 21)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 21), Ceil), Floor), *(themWeightsPtr + 21));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 22), Ceil), Floor), *(featureWeightsPtr + 22)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 22), Ceil), Floor), *(themWeightsPtr + 22));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 23), Ceil), Floor), *(featureWeightsPtr + 23)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 23), Ceil), Floor), *(themWeightsPtr + 23));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 24), Ceil), Floor), *(featureWeightsPtr + 24)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 24), Ceil), Floor), *(themWeightsPtr + 24));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 25), Ceil), Floor), *(featureWeightsPtr + 25)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 25), Ceil), Floor), *(themWeightsPtr + 25));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 26), Ceil), Floor), *(featureWeightsPtr + 26)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 26), Ceil), Floor), *(themWeightsPtr + 26));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 27), Ceil), Floor), *(featureWeightsPtr + 27)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 27), Ceil), Floor), *(themWeightsPtr + 27));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 28), Ceil), Floor), *(featureWeightsPtr + 28)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 28), Ceil), Floor), *(themWeightsPtr + 28));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 29), Ceil), Floor), *(featureWeightsPtr + 29)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 29), Ceil), Floor), *(themWeightsPtr + 29));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 30), Ceil), Floor), *(featureWeightsPtr + 30)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 30), Ceil), Floor), *(themWeightsPtr + 30));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 31), Ceil), Floor), *(featureWeightsPtr + 31)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 31), Ceil), Floor), *(themWeightsPtr + 31));
#if !AVX512
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 32), Ceil), Floor), *(featureWeightsPtr + 32)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 32), Ceil), Floor), *(themWeightsPtr + 32));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 33), Ceil), Floor), *(featureWeightsPtr + 33)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 33), Ceil), Floor), *(themWeightsPtr + 33));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 34), Ceil), Floor), *(featureWeightsPtr + 34)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 34), Ceil), Floor), *(themWeightsPtr + 34));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 35), Ceil), Floor), *(featureWeightsPtr + 35)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 35), Ceil), Floor), *(themWeightsPtr + 35));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 36), Ceil), Floor), *(featureWeightsPtr + 36)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 36), Ceil), Floor), *(themWeightsPtr + 36));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 37), Ceil), Floor), *(featureWeightsPtr + 37)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 37), Ceil), Floor), *(themWeightsPtr + 37));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 38), Ceil), Floor), *(featureWeightsPtr + 38)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 38), Ceil), Floor), *(themWeightsPtr + 38));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 39), Ceil), Floor), *(featureWeightsPtr + 39)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 39), Ceil), Floor), *(themWeightsPtr + 39));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 40), Ceil), Floor), *(featureWeightsPtr + 40)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 40), Ceil), Floor), *(themWeightsPtr + 40));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 41), Ceil), Floor), *(featureWeightsPtr + 41)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 41), Ceil), Floor), *(themWeightsPtr + 41));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 42), Ceil), Floor), *(featureWeightsPtr + 42)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 42), Ceil), Floor), *(themWeightsPtr + 42));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 43), Ceil), Floor), *(featureWeightsPtr + 43)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 43), Ceil), Floor), *(themWeightsPtr + 43));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 44), Ceil), Floor), *(featureWeightsPtr + 44)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 44), Ceil), Floor), *(themWeightsPtr + 44));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 45), Ceil), Floor), *(featureWeightsPtr + 45)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 45), Ceil), Floor), *(themWeightsPtr + 45));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 46), Ceil), Floor), *(featureWeightsPtr + 46)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 46), Ceil), Floor), *(themWeightsPtr + 46));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 47), Ceil), Floor), *(featureWeightsPtr + 47)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 47), Ceil), Floor), *(themWeightsPtr + 47));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 48), Ceil), Floor), *(featureWeightsPtr + 48)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 48), Ceil), Floor), *(themWeightsPtr + 48));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 49), Ceil), Floor), *(featureWeightsPtr + 49)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 49), Ceil), Floor), *(themWeightsPtr + 49));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 50), Ceil), Floor), *(featureWeightsPtr + 50)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 50), Ceil), Floor), *(themWeightsPtr + 50));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 51), Ceil), Floor), *(featureWeightsPtr + 51)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 51), Ceil), Floor), *(themWeightsPtr + 51));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 52), Ceil), Floor), *(featureWeightsPtr + 52)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 52), Ceil), Floor), *(themWeightsPtr + 52));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 53), Ceil), Floor), *(featureWeightsPtr + 53)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 53), Ceil), Floor), *(themWeightsPtr + 53));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 54), Ceil), Floor), *(featureWeightsPtr + 54)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 54), Ceil), Floor), *(themWeightsPtr + 54));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 55), Ceil), Floor), *(featureWeightsPtr + 55)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 55), Ceil), Floor), *(themWeightsPtr + 55));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 56), Ceil), Floor), *(featureWeightsPtr + 56)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 56), Ceil), Floor), *(themWeightsPtr + 56));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 57), Ceil), Floor), *(featureWeightsPtr + 57)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 57), Ceil), Floor), *(themWeightsPtr + 57));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 58), Ceil), Floor), *(featureWeightsPtr + 58)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 58), Ceil), Floor), *(themWeightsPtr + 58));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 59), Ceil), Floor), *(featureWeightsPtr + 59)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 59), Ceil), Floor), *(themWeightsPtr + 59));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 60), Ceil), Floor), *(featureWeightsPtr + 60)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 60), Ceil), Floor), *(themWeightsPtr + 60));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 61), Ceil), Floor), *(featureWeightsPtr + 61)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 61), Ceil), Floor), *(themWeightsPtr + 61));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 62), Ceil), Floor), *(featureWeightsPtr + 62)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 62), Ceil), Floor), *(themWeightsPtr + 62));
        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + 63), Ceil), Floor), *(featureWeightsPtr + 63)) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + 63), Ceil), Floor), *(themWeightsPtr + 63));
#endif


        return VectorType.Sum(sum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubAdd(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub1, VectorShort* add1)
    {
        *(dest + 0) = *(source + 0) - *(sub1 + 0) + *(add1 + 0);
        *(dest + 1) = *(source + 1) - *(sub1 + 1) + *(add1 + 1);
        *(dest + 2) = *(source + 2) - *(sub1 + 2) + *(add1 + 2);
        *(dest + 3) = *(source + 3) - *(sub1 + 3) + *(add1 + 3);
        *(dest + 4) = *(source + 4) - *(sub1 + 4) + *(add1 + 4);
        *(dest + 5) = *(source + 5) - *(sub1 + 5) + *(add1 + 5);
        *(dest + 6) = *(source + 6) - *(sub1 + 6) + *(add1 + 6);
        *(dest + 7) = *(source + 7) - *(sub1 + 7) + *(add1 + 7);
        *(dest + 8) = *(source + 8) - *(sub1 + 8) + *(add1 + 8);
        *(dest + 9) = *(source + 9) - *(sub1 + 9) + *(add1 + 9);
        *(dest + 10) = *(source + 10) - *(sub1 + 10) + *(add1 + 10);
        *(dest + 11) = *(source + 11) - *(sub1 + 11) + *(add1 + 11);
        *(dest + 12) = *(source + 12) - *(sub1 + 12) + *(add1 + 12);
        *(dest + 13) = *(source + 13) - *(sub1 + 13) + *(add1 + 13);
        *(dest + 14) = *(source + 14) - *(sub1 + 14) + *(add1 + 14);
        *(dest + 15) = *(source + 15) - *(sub1 + 15) + *(add1 + 15);
        *(dest + 16) = *(source + 16) - *(sub1 + 16) + *(add1 + 16);
        *(dest + 17) = *(source + 17) - *(sub1 + 17) + *(add1 + 17);
        *(dest + 18) = *(source + 18) - *(sub1 + 18) + *(add1 + 18);
        *(dest + 19) = *(source + 19) - *(sub1 + 19) + *(add1 + 19);
        *(dest + 20) = *(source + 20) - *(sub1 + 20) + *(add1 + 20);
        *(dest + 21) = *(source + 21) - *(sub1 + 21) + *(add1 + 21);
        *(dest + 22) = *(source + 22) - *(sub1 + 22) + *(add1 + 22);
        *(dest + 23) = *(source + 23) - *(sub1 + 23) + *(add1 + 23);
        *(dest + 24) = *(source + 24) - *(sub1 + 24) + *(add1 + 24);
        *(dest + 25) = *(source + 25) - *(sub1 + 25) + *(add1 + 25);
        *(dest + 26) = *(source + 26) - *(sub1 + 26) + *(add1 + 26);
        *(dest + 27) = *(source + 27) - *(sub1 + 27) + *(add1 + 27);
        *(dest + 28) = *(source + 28) - *(sub1 + 28) + *(add1 + 28);
        *(dest + 29) = *(source + 29) - *(sub1 + 29) + *(add1 + 29);
        *(dest + 30) = *(source + 30) - *(sub1 + 30) + *(add1 + 30);
        *(dest + 31) = *(source + 31) - *(sub1 + 31) + *(add1 + 31);
#if !AVX512
        *(dest + 32) = *(source + 32) - *(sub1 + 32) + *(add1 + 32);
        *(dest + 33) = *(source + 33) - *(sub1 + 33) + *(add1 + 33);
        *(dest + 34) = *(source + 34) - *(sub1 + 34) + *(add1 + 34);
        *(dest + 35) = *(source + 35) - *(sub1 + 35) + *(add1 + 35);
        *(dest + 36) = *(source + 36) - *(sub1 + 36) + *(add1 + 36);
        *(dest + 37) = *(source + 37) - *(sub1 + 37) + *(add1 + 37);
        *(dest + 38) = *(source + 38) - *(sub1 + 38) + *(add1 + 38);
        *(dest + 39) = *(source + 39) - *(sub1 + 39) + *(add1 + 39);
        *(dest + 40) = *(source + 40) - *(sub1 + 40) + *(add1 + 40);
        *(dest + 41) = *(source + 41) - *(sub1 + 41) + *(add1 + 41);
        *(dest + 42) = *(source + 42) - *(sub1 + 42) + *(add1 + 42);
        *(dest + 43) = *(source + 43) - *(sub1 + 43) + *(add1 + 43);
        *(dest + 44) = *(source + 44) - *(sub1 + 44) + *(add1 + 44);
        *(dest + 45) = *(source + 45) - *(sub1 + 45) + *(add1 + 45);
        *(dest + 46) = *(source + 46) - *(sub1 + 46) + *(add1 + 46);
        *(dest + 47) = *(source + 47) - *(sub1 + 47) + *(add1 + 47);
        *(dest + 48) = *(source + 48) - *(sub1 + 48) + *(add1 + 48);
        *(dest + 49) = *(source + 49) - *(sub1 + 49) + *(add1 + 49);
        *(dest + 50) = *(source + 50) - *(sub1 + 50) + *(add1 + 50);
        *(dest + 51) = *(source + 51) - *(sub1 + 51) + *(add1 + 51);
        *(dest + 52) = *(source + 52) - *(sub1 + 52) + *(add1 + 52);
        *(dest + 53) = *(source + 53) - *(sub1 + 53) + *(add1 + 53);
        *(dest + 54) = *(source + 54) - *(sub1 + 54) + *(add1 + 54);
        *(dest + 55) = *(source + 55) - *(sub1 + 55) + *(add1 + 55);
        *(dest + 56) = *(source + 56) - *(sub1 + 56) + *(add1 + 56);
        *(dest + 57) = *(source + 57) - *(sub1 + 57) + *(add1 + 57);
        *(dest + 58) = *(source + 58) - *(sub1 + 58) + *(add1 + 58);
        *(dest + 59) = *(source + 59) - *(sub1 + 59) + *(add1 + 59);
        *(dest + 60) = *(source + 60) - *(sub1 + 60) + *(add1 + 60);
        *(dest + 61) = *(source + 61) - *(sub1 + 61) + *(add1 + 61);
        *(dest + 62) = *(source + 62) - *(sub1 + 62) + *(add1 + 62);
        *(dest + 63) = *(source + 63) - *(sub1 + 63) + *(add1 + 63);
#endif

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAdd(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1)
    {
        *(dest + 0) = *(source + 0) - *(sub1 + 0) + *(add1 + 0) - *(sub2 + 0);
        *(dest + 1) = *(source + 1) - *(sub1 + 1) + *(add1 + 1) - *(sub2 + 1);
        *(dest + 2) = *(source + 2) - *(sub1 + 2) + *(add1 + 2) - *(sub2 + 2);
        *(dest + 3) = *(source + 3) - *(sub1 + 3) + *(add1 + 3) - *(sub2 + 3);
        *(dest + 4) = *(source + 4) - *(sub1 + 4) + *(add1 + 4) - *(sub2 + 4);
        *(dest + 5) = *(source + 5) - *(sub1 + 5) + *(add1 + 5) - *(sub2 + 5);
        *(dest + 6) = *(source + 6) - *(sub1 + 6) + *(add1 + 6) - *(sub2 + 6);
        *(dest + 7) = *(source + 7) - *(sub1 + 7) + *(add1 + 7) - *(sub2 + 7);
        *(dest + 8) = *(source + 8) - *(sub1 + 8) + *(add1 + 8) - *(sub2 + 8);
        *(dest + 9) = *(source + 9) - *(sub1 + 9) + *(add1 + 9) - *(sub2 + 9);
        *(dest + 10) = *(source + 10) - *(sub1 + 10) + *(add1 + 10) - *(sub2 + 10);
        *(dest + 11) = *(source + 11) - *(sub1 + 11) + *(add1 + 11) - *(sub2 + 11);
        *(dest + 12) = *(source + 12) - *(sub1 + 12) + *(add1 + 12) - *(sub2 + 12);
        *(dest + 13) = *(source + 13) - *(sub1 + 13) + *(add1 + 13) - *(sub2 + 13);
        *(dest + 14) = *(source + 14) - *(sub1 + 14) + *(add1 + 14) - *(sub2 + 14);
        *(dest + 15) = *(source + 15) - *(sub1 + 15) + *(add1 + 15) - *(sub2 + 15);
        *(dest + 16) = *(source + 16) - *(sub1 + 16) + *(add1 + 16) - *(sub2 + 16);
        *(dest + 17) = *(source + 17) - *(sub1 + 17) + *(add1 + 17) - *(sub2 + 17);
        *(dest + 18) = *(source + 18) - *(sub1 + 18) + *(add1 + 18) - *(sub2 + 18);
        *(dest + 19) = *(source + 19) - *(sub1 + 19) + *(add1 + 19) - *(sub2 + 19);
        *(dest + 20) = *(source + 20) - *(sub1 + 20) + *(add1 + 20) - *(sub2 + 20);
        *(dest + 21) = *(source + 21) - *(sub1 + 21) + *(add1 + 21) - *(sub2 + 21);
        *(dest + 22) = *(source + 22) - *(sub1 + 22) + *(add1 + 22) - *(sub2 + 22);
        *(dest + 23) = *(source + 23) - *(sub1 + 23) + *(add1 + 23) - *(sub2 + 23);
        *(dest + 24) = *(source + 24) - *(sub1 + 24) + *(add1 + 24) - *(sub2 + 24);
        *(dest + 25) = *(source + 25) - *(sub1 + 25) + *(add1 + 25) - *(sub2 + 25);
        *(dest + 26) = *(source + 26) - *(sub1 + 26) + *(add1 + 26) - *(sub2 + 26);
        *(dest + 27) = *(source + 27) - *(sub1 + 27) + *(add1 + 27) - *(sub2 + 27);
        *(dest + 28) = *(source + 28) - *(sub1 + 28) + *(add1 + 28) - *(sub2 + 28);
        *(dest + 29) = *(source + 29) - *(sub1 + 29) + *(add1 + 29) - *(sub2 + 29);
        *(dest + 30) = *(source + 30) - *(sub1 + 30) + *(add1 + 30) - *(sub2 + 30);
        *(dest + 31) = *(source + 31) - *(sub1 + 31) + *(add1 + 31) - *(sub2 + 31);
#if !AVX512
        *(dest + 32) = *(source + 32) - *(sub1 + 32) + *(add1 + 32) - *(sub2 + 32);
        *(dest + 33) = *(source + 33) - *(sub1 + 33) + *(add1 + 33) - *(sub2 + 33);
        *(dest + 34) = *(source + 34) - *(sub1 + 34) + *(add1 + 34) - *(sub2 + 34);
        *(dest + 35) = *(source + 35) - *(sub1 + 35) + *(add1 + 35) - *(sub2 + 35);
        *(dest + 36) = *(source + 36) - *(sub1 + 36) + *(add1 + 36) - *(sub2 + 36);
        *(dest + 37) = *(source + 37) - *(sub1 + 37) + *(add1 + 37) - *(sub2 + 37);
        *(dest + 38) = *(source + 38) - *(sub1 + 38) + *(add1 + 38) - *(sub2 + 38);
        *(dest + 39) = *(source + 39) - *(sub1 + 39) + *(add1 + 39) - *(sub2 + 39);
        *(dest + 40) = *(source + 40) - *(sub1 + 40) + *(add1 + 40) - *(sub2 + 40);
        *(dest + 41) = *(source + 41) - *(sub1 + 41) + *(add1 + 41) - *(sub2 + 41);
        *(dest + 42) = *(source + 42) - *(sub1 + 42) + *(add1 + 42) - *(sub2 + 42);
        *(dest + 43) = *(source + 43) - *(sub1 + 43) + *(add1 + 43) - *(sub2 + 43);
        *(dest + 44) = *(source + 44) - *(sub1 + 44) + *(add1 + 44) - *(sub2 + 44);
        *(dest + 45) = *(source + 45) - *(sub1 + 45) + *(add1 + 45) - *(sub2 + 45);
        *(dest + 46) = *(source + 46) - *(sub1 + 46) + *(add1 + 46) - *(sub2 + 46);
        *(dest + 47) = *(source + 47) - *(sub1 + 47) + *(add1 + 47) - *(sub2 + 47);
        *(dest + 48) = *(source + 48) - *(sub1 + 48) + *(add1 + 48) - *(sub2 + 48);
        *(dest + 49) = *(source + 49) - *(sub1 + 49) + *(add1 + 49) - *(sub2 + 49);
        *(dest + 50) = *(source + 50) - *(sub1 + 50) + *(add1 + 50) - *(sub2 + 50);
        *(dest + 51) = *(source + 51) - *(sub1 + 51) + *(add1 + 51) - *(sub2 + 51);
        *(dest + 52) = *(source + 52) - *(sub1 + 52) + *(add1 + 52) - *(sub2 + 52);
        *(dest + 53) = *(source + 53) - *(sub1 + 53) + *(add1 + 53) - *(sub2 + 53);
        *(dest + 54) = *(source + 54) - *(sub1 + 54) + *(add1 + 54) - *(sub2 + 54);
        *(dest + 55) = *(source + 55) - *(sub1 + 55) + *(add1 + 55) - *(sub2 + 55);
        *(dest + 56) = *(source + 56) - *(sub1 + 56) + *(add1 + 56) - *(sub2 + 56);
        *(dest + 57) = *(source + 57) - *(sub1 + 57) + *(add1 + 57) - *(sub2 + 57);
        *(dest + 58) = *(source + 58) - *(sub1 + 58) + *(add1 + 58) - *(sub2 + 58);
        *(dest + 59) = *(source + 59) - *(sub1 + 59) + *(add1 + 59) - *(sub2 + 59);
        *(dest + 60) = *(source + 60) - *(sub1 + 60) + *(add1 + 60) - *(sub2 + 60);
        *(dest + 61) = *(source + 61) - *(sub1 + 61) + *(add1 + 61) - *(sub2 + 61);
        *(dest + 62) = *(source + 62) - *(sub1 + 62) + *(add1 + 62) - *(sub2 + 62);
        *(dest + 63) = *(source + 63) - *(sub1 + 63) + *(add1 + 63) - *(sub2 + 63);
#endif

    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAddAdd(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1, VectorShort* add2)
    {
        *(dest + 0) = *(source + 0) - *(sub1 + 0) + *(add1 + 0) - *(sub2 + 0) + *(add2 + 0);
        *(dest + 1) = *(source + 1) - *(sub1 + 1) + *(add1 + 1) - *(sub2 + 1) + *(add2 + 1);
        *(dest + 2) = *(source + 2) - *(sub1 + 2) + *(add1 + 2) - *(sub2 + 2) + *(add2 + 2);
        *(dest + 3) = *(source + 3) - *(sub1 + 3) + *(add1 + 3) - *(sub2 + 3) + *(add2 + 3);
        *(dest + 4) = *(source + 4) - *(sub1 + 4) + *(add1 + 4) - *(sub2 + 4) + *(add2 + 4);
        *(dest + 5) = *(source + 5) - *(sub1 + 5) + *(add1 + 5) - *(sub2 + 5) + *(add2 + 5);
        *(dest + 6) = *(source + 6) - *(sub1 + 6) + *(add1 + 6) - *(sub2 + 6) + *(add2 + 6);
        *(dest + 7) = *(source + 7) - *(sub1 + 7) + *(add1 + 7) - *(sub2 + 7) + *(add2 + 7);
        *(dest + 8) = *(source + 8) - *(sub1 + 8) + *(add1 + 8) - *(sub2 + 8) + *(add2 + 8);
        *(dest + 9) = *(source + 9) - *(sub1 + 9) + *(add1 + 9) - *(sub2 + 9) + *(add2 + 9);
        *(dest + 10) = *(source + 10) - *(sub1 + 10) + *(add1 + 10) - *(sub2 + 10) + *(add2 + 10);
        *(dest + 11) = *(source + 11) - *(sub1 + 11) + *(add1 + 11) - *(sub2 + 11) + *(add2 + 11);
        *(dest + 12) = *(source + 12) - *(sub1 + 12) + *(add1 + 12) - *(sub2 + 12) + *(add2 + 12);
        *(dest + 13) = *(source + 13) - *(sub1 + 13) + *(add1 + 13) - *(sub2 + 13) + *(add2 + 13);
        *(dest + 14) = *(source + 14) - *(sub1 + 14) + *(add1 + 14) - *(sub2 + 14) + *(add2 + 14);
        *(dest + 15) = *(source + 15) - *(sub1 + 15) + *(add1 + 15) - *(sub2 + 15) + *(add2 + 15);
        *(dest + 16) = *(source + 16) - *(sub1 + 16) + *(add1 + 16) - *(sub2 + 16) + *(add2 + 16);
        *(dest + 17) = *(source + 17) - *(sub1 + 17) + *(add1 + 17) - *(sub2 + 17) + *(add2 + 17);
        *(dest + 18) = *(source + 18) - *(sub1 + 18) + *(add1 + 18) - *(sub2 + 18) + *(add2 + 18);
        *(dest + 19) = *(source + 19) - *(sub1 + 19) + *(add1 + 19) - *(sub2 + 19) + *(add2 + 19);
        *(dest + 20) = *(source + 20) - *(sub1 + 20) + *(add1 + 20) - *(sub2 + 20) + *(add2 + 20);
        *(dest + 21) = *(source + 21) - *(sub1 + 21) + *(add1 + 21) - *(sub2 + 21) + *(add2 + 21);
        *(dest + 22) = *(source + 22) - *(sub1 + 22) + *(add1 + 22) - *(sub2 + 22) + *(add2 + 22);
        *(dest + 23) = *(source + 23) - *(sub1 + 23) + *(add1 + 23) - *(sub2 + 23) + *(add2 + 23);
        *(dest + 24) = *(source + 24) - *(sub1 + 24) + *(add1 + 24) - *(sub2 + 24) + *(add2 + 24);
        *(dest + 25) = *(source + 25) - *(sub1 + 25) + *(add1 + 25) - *(sub2 + 25) + *(add2 + 25);
        *(dest + 26) = *(source + 26) - *(sub1 + 26) + *(add1 + 26) - *(sub2 + 26) + *(add2 + 26);
        *(dest + 27) = *(source + 27) - *(sub1 + 27) + *(add1 + 27) - *(sub2 + 27) + *(add2 + 27);
        *(dest + 28) = *(source + 28) - *(sub1 + 28) + *(add1 + 28) - *(sub2 + 28) + *(add2 + 28);
        *(dest + 29) = *(source + 29) - *(sub1 + 29) + *(add1 + 29) - *(sub2 + 29) + *(add2 + 29);
        *(dest + 30) = *(source + 30) - *(sub1 + 30) + *(add1 + 30) - *(sub2 + 30) + *(add2 + 30);
        *(dest + 31) = *(source + 31) - *(sub1 + 31) + *(add1 + 31) - *(sub2 + 31) + *(add2 + 31);
#if !AVX512
        *(dest + 32) = *(source + 32) - *(sub1 + 32) + *(add1 + 32) - *(sub2 + 32) + *(add2 + 32);
        *(dest + 33) = *(source + 33) - *(sub1 + 33) + *(add1 + 33) - *(sub2 + 33) + *(add2 + 33);
        *(dest + 34) = *(source + 34) - *(sub1 + 34) + *(add1 + 34) - *(sub2 + 34) + *(add2 + 34);
        *(dest + 35) = *(source + 35) - *(sub1 + 35) + *(add1 + 35) - *(sub2 + 35) + *(add2 + 35);
        *(dest + 36) = *(source + 36) - *(sub1 + 36) + *(add1 + 36) - *(sub2 + 36) + *(add2 + 36);
        *(dest + 37) = *(source + 37) - *(sub1 + 37) + *(add1 + 37) - *(sub2 + 37) + *(add2 + 37);
        *(dest + 38) = *(source + 38) - *(sub1 + 38) + *(add1 + 38) - *(sub2 + 38) + *(add2 + 38);
        *(dest + 39) = *(source + 39) - *(sub1 + 39) + *(add1 + 39) - *(sub2 + 39) + *(add2 + 39);
        *(dest + 40) = *(source + 40) - *(sub1 + 40) + *(add1 + 40) - *(sub2 + 40) + *(add2 + 40);
        *(dest + 41) = *(source + 41) - *(sub1 + 41) + *(add1 + 41) - *(sub2 + 41) + *(add2 + 41);
        *(dest + 42) = *(source + 42) - *(sub1 + 42) + *(add1 + 42) - *(sub2 + 42) + *(add2 + 42);
        *(dest + 43) = *(source + 43) - *(sub1 + 43) + *(add1 + 43) - *(sub2 + 43) + *(add2 + 43);
        *(dest + 44) = *(source + 44) - *(sub1 + 44) + *(add1 + 44) - *(sub2 + 44) + *(add2 + 44);
        *(dest + 45) = *(source + 45) - *(sub1 + 45) + *(add1 + 45) - *(sub2 + 45) + *(add2 + 45);
        *(dest + 46) = *(source + 46) - *(sub1 + 46) + *(add1 + 46) - *(sub2 + 46) + *(add2 + 46);
        *(dest + 47) = *(source + 47) - *(sub1 + 47) + *(add1 + 47) - *(sub2 + 47) + *(add2 + 47);
        *(dest + 48) = *(source + 48) - *(sub1 + 48) + *(add1 + 48) - *(sub2 + 48) + *(add2 + 48);
        *(dest + 49) = *(source + 49) - *(sub1 + 49) + *(add1 + 49) - *(sub2 + 49) + *(add2 + 49);
        *(dest + 50) = *(source + 50) - *(sub1 + 50) + *(add1 + 50) - *(sub2 + 50) + *(add2 + 50);
        *(dest + 51) = *(source + 51) - *(sub1 + 51) + *(add1 + 51) - *(sub2 + 51) + *(add2 + 51);
        *(dest + 52) = *(source + 52) - *(sub1 + 52) + *(add1 + 52) - *(sub2 + 52) + *(add2 + 52);
        *(dest + 53) = *(source + 53) - *(sub1 + 53) + *(add1 + 53) - *(sub2 + 53) + *(add2 + 53);
        *(dest + 54) = *(source + 54) - *(sub1 + 54) + *(add1 + 54) - *(sub2 + 54) + *(add2 + 54);
        *(dest + 55) = *(source + 55) - *(sub1 + 55) + *(add1 + 55) - *(sub2 + 55) + *(add2 + 55);
        *(dest + 56) = *(source + 56) - *(sub1 + 56) + *(add1 + 56) - *(sub2 + 56) + *(add2 + 56);
        *(dest + 57) = *(source + 57) - *(sub1 + 57) + *(add1 + 57) - *(sub2 + 57) + *(add2 + 57);
        *(dest + 58) = *(source + 58) - *(sub1 + 58) + *(add1 + 58) - *(sub2 + 58) + *(add2 + 58);
        *(dest + 59) = *(source + 59) - *(sub1 + 59) + *(add1 + 59) - *(sub2 + 59) + *(add2 + 59);
        *(dest + 60) = *(source + 60) - *(sub1 + 60) + *(add1 + 60) - *(sub2 + 60) + *(add2 + 60);
        *(dest + 61) = *(source + 61) - *(sub1 + 61) + *(add1 + 61) - *(sub2 + 61) + *(add2 + 61);
        *(dest + 62) = *(source + 62) - *(sub1 + 62) + *(add1 + 62) - *(sub2 + 62) + *(add2 + 62);
        *(dest + 63) = *(source + 63) - *(sub1 + 63) + *(add1 + 63) - *(sub2 + 63) + *(add2 + 63);
#endif

    }
}
