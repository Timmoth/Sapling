using Sapling.Engine.Search;
using System.Globalization;
using System.Reflection;

namespace Sapling.Engine.Tuning
{
    public class SpsaParameter
    {
        public string Name;
        public string Type;
        public string DefaultValue;
        public string MinValue;
        public string MaxValue;
        public FieldInfo FieldHandle;
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class SpsaMinValueAttribute : Attribute
    {
        public string MinValue { get; }

        public SpsaMinValueAttribute(string minValue)
        {
            MinValue = minValue;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class SpsaIgnoreAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class SpsaMaxValueAttribute : Attribute
    {
        public string MaxValue { get; }

        public SpsaMaxValueAttribute(string maxValue)
        {
            MaxValue = maxValue;
        }
    }


    internal static class SpsaDefaults
    {
        public const int ReverseFutilityPruningMargin = 67;
        public const int ReverseFutilityPruningDepth = 7;
        public const int NullMovePruningDepth = 2;
        public const float NullMovePruningReductionA = 3;
        public const float NullMovePruningReductionB = 4;
        public const float NullMovePruningReductionC = 3;
        public const int RazorMarginA = 58;
        public const int RazorMarginB = 378;
        public const int InternalIterativeDeepeningDepth = 2;
        public const int LateMovePruningConstant = 8;
        public const int LateMoveReductionMinDepth = 3;
        public const int LateMoveReductionMinMoves = 2;
        public const float LateMoveReductionInterestingA = 0.188786f;
        public const float LateMoveReductionInterestingB = 2.4956496f;
        public const float LateMoveReductionA = 1.315961f;
        public const float LateMoveReductionB = 2.7831153f;
        public const float LateMoveReductionTtExactScale = 0.75f;
        public const int HistoryHeuristicMaxHistory = 9532;
        public const int HistoryHeuristicBonusMax = 406;
        public const int HistoryHeuristicBonusCoeff = 83;
        public const int MoveOrderingBestMoveBias = 239130;
        public const int MoveOrderingEnPassantMoveBias = 78671;
        public const int MoveOrderingWinningCaptureBias = 134191;
        public const int MoveOrderingLosingCaptureBias = 16845;
        public const int MoveOrderingPromoteBias = 46577;
        public const int MoveOrderingCapturePromoteBias = 35558;
        public const int MoveOrderingKillerABias = 66845;
        public const int MoveOrderingCounterMoveBias = 87003;
        public const int InterestingNegaMaxMoveScore = 40775;
        public const int InterestingQuiescenceMoveScore = 35432;
        public const int ProbCutBetaMargin = 220;
        public const int ProbCutMinDepth = 3;
        public const int AsperationWindowA = 37;
        public const int AsperationWindowB = 59;
        public const int AsperationWindowC = 279;
        public const int AsperationWindowD = 847;
        public const int AsperationWindowE = 2785;
    }

    public static unsafe class SpsaOptions
    {
#if OpenBench
        [SpsaMinValue("60"), SpsaMaxValue("80")]
        public static int ReverseFutilityPruningMargin = SpsaDefaults.ReverseFutilityPruningMargin;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static int ReverseFutilityPruningDepth = SpsaDefaults.ReverseFutilityPruningDepth;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static int NullMovePruningDepth = SpsaDefaults.NullMovePruningDepth;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static float NullMovePruningReductionA = SpsaDefaults.NullMovePruningReductionA;

        [SpsaMinValue("0.1"), SpsaMaxValue("10")]
        public static float NullMovePruningReductionB = SpsaDefaults.NullMovePruningReductionB;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static float NullMovePruningReductionC = SpsaDefaults.NullMovePruningReductionC;

        [SpsaMinValue("50"), SpsaMaxValue("100")]
        public static int RazorMarginA = SpsaDefaults.RazorMarginA;

        [SpsaMinValue("250"), SpsaMaxValue("400")]
        public static int RazorMarginB = SpsaDefaults.RazorMarginB;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static int InternalIterativeDeepeningDepth = SpsaDefaults.InternalIterativeDeepeningDepth;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static int LateMovePruningConstant = SpsaDefaults.LateMovePruningConstant;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static int LateMoveReductionMinDepth = SpsaDefaults.LateMoveReductionMinDepth;

        [SpsaMinValue("0"), SpsaMaxValue("10")]
        public static int LateMoveReductionMinMoves = SpsaDefaults.LateMoveReductionMinMoves;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionInterestingA = SpsaDefaults.LateMoveReductionInterestingA;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionInterestingB = SpsaDefaults.LateMoveReductionInterestingB;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionA = SpsaDefaults.LateMoveReductionA;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionB = SpsaDefaults.LateMoveReductionB;

        [SpsaMinValue("0"), SpsaMaxValue("1")]
        public static float LateMoveReductionTtExactScale = SpsaDefaults.LateMoveReductionTtExactScale;

        [SpsaMinValue("7000"), SpsaMaxValue("10000")]
        public static int HistoryHeuristicMaxHistory = SpsaDefaults.HistoryHeuristicMaxHistory;

        [SpsaMinValue("256"), SpsaMaxValue("1000")]
        public static int HistoryHeuristicBonusMax = SpsaDefaults.HistoryHeuristicBonusMax;

        [SpsaMinValue("50"), SpsaMaxValue("256")]
        public static int HistoryHeuristicBonusCoeff = SpsaDefaults.HistoryHeuristicBonusCoeff;

        [SpsaMinValue("100000"), SpsaMaxValue("300000")]
        public static int MoveOrderingBestMoveBias = SpsaDefaults.MoveOrderingBestMoveBias;

        [SpsaMinValue("50000"), SpsaMaxValue("100000")]
        public static int MoveOrderingEnPassantMoveBias = SpsaDefaults.MoveOrderingEnPassantMoveBias;

        [SpsaMinValue("50000"), SpsaMaxValue("200000")]
        public static int MoveOrderingWinningCaptureBias = SpsaDefaults.MoveOrderingWinningCaptureBias;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int MoveOrderingLosingCaptureBias = SpsaDefaults.MoveOrderingLosingCaptureBias;

        [SpsaMinValue("30000"), SpsaMaxValue("100000")]
        public static int MoveOrderingPromoteBias = SpsaDefaults.MoveOrderingPromoteBias;

        [SpsaMinValue("30000"), SpsaMaxValue("100000")]
        public static int MoveOrderingCapturePromoteBias = SpsaDefaults.MoveOrderingCapturePromoteBias;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int MoveOrderingKillerABias = SpsaDefaults.MoveOrderingKillerABias;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int MoveOrderingCounterMoveBias = SpsaDefaults.MoveOrderingCounterMoveBias;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int InterestingNegaMaxMoveScore = SpsaDefaults.InterestingNegaMaxMoveScore;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int InterestingQuiescenceMoveScore = SpsaDefaults.InterestingQuiescenceMoveScore;

        [SpsaMinValue("200"), SpsaMaxValue("300")]
        public static int ProbCutBetaMargin = SpsaDefaults.ProbCutBetaMargin;

        [SpsaMinValue("0"), SpsaMaxValue("6")]
        public static int ProbCutMinDepth = SpsaDefaults.ProbCutMinDepth;

        [SpsaMinValue("30"), SpsaMaxValue("100")]
        public static int AsperationWindowA = SpsaDefaults.AsperationWindowA;

        [SpsaMinValue("50"), SpsaMaxValue("200")]
        public static int AsperationWindowB = SpsaDefaults.AsperationWindowB;

        [SpsaMinValue("100"), SpsaMaxValue("500")]
        public static int AsperationWindowC = SpsaDefaults.AsperationWindowC;

        [SpsaMinValue("400"), SpsaMaxValue("1500")]
        public static int AsperationWindowD = SpsaDefaults.AsperationWindowD;

        [SpsaMinValue("1400"), SpsaMaxValue("3000")]
        public static int AsperationWindowE = SpsaDefaults.AsperationWindowE;
#else
        public const int ReverseFutilityPruningMargin = SpsaDefaults.ReverseFutilityPruningMargin;
        public const int ReverseFutilityPruningDepth = SpsaDefaults.ReverseFutilityPruningDepth;
        public const int NullMovePruningDepth = SpsaDefaults.NullMovePruningDepth;
        public const float NullMovePruningReductionA = SpsaDefaults.NullMovePruningReductionA;
        public const float NullMovePruningReductionB = SpsaDefaults.NullMovePruningReductionB;
        public const float NullMovePruningReductionC = SpsaDefaults.NullMovePruningReductionC;
        public const int RazorMarginA = SpsaDefaults.RazorMarginA;
        public const int RazorMarginB = SpsaDefaults.RazorMarginB;
        public const int InternalIterativeDeepeningDepth = SpsaDefaults.InternalIterativeDeepeningDepth;
        public const int LateMovePruningConstant = SpsaDefaults.LateMovePruningConstant;
        public const int LateMoveReductionMinDepth = SpsaDefaults.LateMoveReductionMinDepth;
        public const int LateMoveReductionMinMoves = SpsaDefaults.LateMoveReductionMinMoves;
        public const float LateMoveReductionInterestingA = SpsaDefaults.LateMoveReductionInterestingA;
        public const float LateMoveReductionInterestingB = SpsaDefaults.LateMoveReductionInterestingB;
        public const float LateMoveReductionA = SpsaDefaults.LateMoveReductionA;
        public const float LateMoveReductionB = SpsaDefaults.LateMoveReductionB;
        public const float LateMoveReductionTtExactScale = SpsaDefaults.LateMoveReductionTtExactScale;
        public const int HistoryHeuristicMaxHistory = SpsaDefaults.HistoryHeuristicMaxHistory;
        public const int HistoryHeuristicBonusMax = SpsaDefaults.HistoryHeuristicBonusMax;
        public const int HistoryHeuristicBonusCoeff = SpsaDefaults.HistoryHeuristicBonusCoeff;
        public const int MoveOrderingBestMoveBias = SpsaDefaults.MoveOrderingBestMoveBias;
        public const int MoveOrderingEnPassantMoveBias = SpsaDefaults.MoveOrderingEnPassantMoveBias;
        public const int MoveOrderingWinningCaptureBias = SpsaDefaults.MoveOrderingWinningCaptureBias;
        public const int MoveOrderingLosingCaptureBias = SpsaDefaults.MoveOrderingLosingCaptureBias;
        public const int MoveOrderingPromoteBias = SpsaDefaults.MoveOrderingPromoteBias;
        public const int MoveOrderingCapturePromoteBias = SpsaDefaults.MoveOrderingCapturePromoteBias;
        public const int MoveOrderingKillerABias = SpsaDefaults.MoveOrderingKillerABias;
        public const int MoveOrderingCounterMoveBias = SpsaDefaults.MoveOrderingCounterMoveBias;
        public const int InterestingNegaMaxMoveScore = SpsaDefaults.InterestingNegaMaxMoveScore;
        public const int InterestingQuiescenceMoveScore = SpsaDefaults.InterestingQuiescenceMoveScore;
        public const int ProbCutBetaMargin = SpsaDefaults.ProbCutBetaMargin;
        public const int ProbCutMinDepth = SpsaDefaults.ProbCutMinDepth;
        public const int AsperationWindowA = SpsaDefaults.AsperationWindowA;
        public const int AsperationWindowB = SpsaDefaults.AsperationWindowB;
        public const int AsperationWindowC = SpsaDefaults.AsperationWindowC;
        public const int AsperationWindowD = SpsaDefaults.AsperationWindowD;
        public const int AsperationWindowE = SpsaDefaults.AsperationWindowE;
#endif

        public static int* LateMovePruningInterestingReductionTable;
        public static int* LateMovePruningReductionTable;
        public static int* NullMovePruningReductionTable;
        static SpsaOptions()
        {
            NullMovePruningReductionTable = MemoryHelpers.Allocate<int>(Constants.MaxSearchDepth);
            LateMovePruningReductionTable = MemoryHelpers.Allocate<int>(Constants.MaxSearchDepth * 218);
            LateMovePruningInterestingReductionTable = MemoryHelpers.Allocate<int>(Constants.MaxSearchDepth * 218);
            UpdateNullMovePruningReductionTable();
            UpdateLateMovePruningReductionTable();
        }

        public static void UpdateNullMovePruningReductionTable()
        {
            for (var i = 0; i < Constants.MaxSearchDepth; i++)
            {
                NullMovePruningReductionTable[i] = (int)Math.Round(Math.Max(0, ((i - NullMovePruningReductionA) / NullMovePruningReductionB) + NullMovePruningReductionC));
            }
        }

        public static void UpdateLateMovePruningReductionTable()
        {
            for (var i = 0; i < Constants.MaxSearchDepth; i++)
            {
                for (var j = 0; j < 218; j++)
                {
                    LateMovePruningInterestingReductionTable[i*218 +j] = (int)Math.Round(LateMoveReductionInterestingA + (Math.Log(i) * Math.Log(j)) / LateMoveReductionInterestingB);
                    LateMovePruningReductionTable[i * 218 + j] = (int)Math.Round(LateMoveReductionA + (Math.Log(i) * Math.Log(j)) / LateMoveReductionB);
                }
            }
        }
    }

    public static class SpsaTuner
    {
        public static Dictionary<string, SpsaParameter> TuningParameters = new();

        static SpsaTuner()
        {
            ProcessUCIOptions();
        }

        public static string FormatParameter(SpsaParameter parameter)
        {
            const double minStepSize = 0.01;
            const double normalLearningRate = 0.02;

            if (parameter.Type == "int")
            {
                var min = int.Parse(parameter.MinValue, CultureInfo.InvariantCulture);
                var max = int.Parse(parameter.MaxValue, CultureInfo.InvariantCulture);
                var ss = Math.Max(minStepSize, (max - min) / 40.0);
                var lr = double.Round(Math.Max(normalLearningRate, normalLearningRate * (0.50 / ss)), 4);

                return
                    $"{parameter.Name}, {parameter.Type}, {parameter.DefaultValue}, {parameter.MinValue}, {parameter.MaxValue}, {ss.ToString(CultureInfo.InvariantCulture)}, {lr.ToString(CultureInfo.InvariantCulture)}";
            }
            else
            {
                var min = double.Parse(parameter.MinValue, CultureInfo.InvariantCulture);
                var max = double.Parse(parameter.MaxValue, CultureInfo.InvariantCulture);
                var ss = Math.Max(minStepSize, (max - min) / 40.0);
                var lr = double.Round(Math.Max(normalLearningRate, normalLearningRate * (0.50 / ss)), 4);

                return
                    $"{parameter.Name}, {parameter.Type}, {parameter.DefaultValue}, {parameter.MinValue}, {parameter.MaxValue}, {ss.ToString(CultureInfo.InvariantCulture)}, {lr.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        public static void ProcessUCIOptions()
        {
            TuningParameters = new Dictionary<string, SpsaParameter>();

            foreach (var field in typeof(SpsaOptions).GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(x => !x.IsLiteral).ToList())
            {

                if (field.FieldType != typeof(int) && field.FieldType != typeof(float))
                {
                    continue;
                }
                var defaultValue = Convert.ToString(field.GetValue(null), CultureInfo.InvariantCulture) ?? "0";
                var minValueAttribute = field.GetCustomAttribute<SpsaMinValueAttribute>();
                var maxValueAttribute = field.GetCustomAttribute<SpsaMaxValueAttribute>();
                var ignoreAttribute = field.GetCustomAttribute<SpsaIgnoreAttribute>();
                if (ignoreAttribute != null)
                {
                    continue;
                }

                var minValue = minValueAttribute?.MinValue ?? defaultValue;
                var maxValue = maxValueAttribute?.MaxValue ?? defaultValue;

                TuningParameters[field.Name.ToLowerInvariant()] = new SpsaParameter()
                {
                    Name = field.Name,
                    DefaultValue = defaultValue,
                    Type = field.FieldType.Name == nameof(Int32) ? "int" : "float",
                    MinValue = minValue,
                    MaxValue = maxValue,
                    FieldHandle = field
                };
            }
        }

        public static List<string> PrintSPSAParams()
        {
            var parameters = new List<string>();
            foreach (var parameter in TuningParameters.Values)
            {
                parameters.Add(FormatParameter(parameter));
            }

            return parameters;
        }

        public static bool SetParameterValue(string[] tokens)
        {
            if (tokens.Length != 5 || tokens[0] != "setoption" || tokens[1] != "name" || tokens[3] != "value" ||
                !TuningParameters.TryGetValue(tokens[2].ToLowerInvariant(), out var parameter))
            {
                return false;
            }

            var changed = false;
            if (parameter.Type == "int")
            {
                if (int.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                    int.TryParse(parameter.MinValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) &&
                    int.TryParse(parameter.MaxValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) &&
                    value >= min && value <= max)
                {
                    changed = (int)parameter.FieldHandle.GetValue(null)! != value;
                    if (changed) parameter.FieldHandle.SetValue(null, value);
                }
            }
            else
            {
                if (float.TryParse(tokens[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                    float.IsFinite(value) &&
                    float.TryParse(parameter.MinValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) &&
                    float.TryParse(parameter.MaxValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var max) &&
                    value >= min && value <= max)
                {
                    changed = (float)parameter.FieldHandle.GetValue(null)! != value;
                    if (changed) parameter.FieldHandle.SetValue(null, value);
                }
            }

            if (!changed) return TryReadParameterValue(parameter, tokens[4]);

            if (parameter.Name is "HistoryHeuristicBonusMax" or "HistoryHeuristicBonusCoeff")
            {
                HistoryHeuristicExtensions.UpdateBonusTable();
            }else if (parameter.Name.Contains("NullMovePruningReduction"))
            {
                SpsaOptions.UpdateNullMovePruningReductionTable();
            }else if (parameter.Name.Contains("LateMoveReduction"))
            {
                SpsaOptions.UpdateLateMovePruningReductionTable();
            }

            ProcessUCIOptions();
            return true;
        }

        private static bool TryReadParameterValue(SpsaParameter parameter, string value)
        {
            if (parameter.Type == "int")
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                       parsed >= int.Parse(parameter.MinValue, CultureInfo.InvariantCulture) &&
                       parsed <= int.Parse(parameter.MaxValue, CultureInfo.InvariantCulture) &&
                       parsed == (int)parameter.FieldHandle.GetValue(null)!;

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue) &&
                   float.IsFinite(floatValue) &&
                   floatValue >= float.Parse(parameter.MinValue, CultureInfo.InvariantCulture) &&
                   floatValue <= float.Parse(parameter.MaxValue, CultureInfo.InvariantCulture) &&
                   floatValue == (float)parameter.FieldHandle.GetValue(null)!;
        }

        public static bool HasParameter(string parameterName)
        {
            return TuningParameters.ContainsKey(parameterName.ToLowerInvariant());
        }
    }

}
