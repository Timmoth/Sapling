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


    public static class SpsaOptions
    {
        [SpsaMinValue("60"), SpsaMaxValue("80")]
        public static int ReverseFutilityPruningMargin = 67;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static int ReverseFutilityPruningDepth = 7;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static int NullMovePruningDepth = 2;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static float NullMovePruningReductionA = 2;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static float NullMovePruningReductionB = 7;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static float NullMovePruningReductionC = 5;

        [SpsaMinValue("50"), SpsaMaxValue("100")]
        public static int RazorMarginA = 57;

        [SpsaMinValue("250"), SpsaMaxValue("400")]
        public static int RazorMarginB = 365;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static int InternalIterativeDeepeningDepth = 2;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static int LateMovePruningConstant = 8;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static int LateMoveReductionMinDepth = 3;

        [SpsaMinValue("0"), SpsaMaxValue("10"), SpsaIgnore()]
        public static int LateMoveReductionMinMoves = 2;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionInterestingA = 0.2f;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionInterestingB = 3.3f;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionA = 1.35f;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionB = 2.75f;

        [SpsaMinValue("7000"), SpsaMaxValue("10000")]
        public static int HistoryHeuristicMaxHistory = 9550;

        [SpsaMinValue("256"), SpsaMaxValue("1000")]
        public static int HistoryHeuristicBonusMax = 424;

        [SpsaMinValue("50"), SpsaMaxValue("256")]
        public static int HistoryHeuristicBonusCoeff = 93;

        [SpsaMinValue("100000"), SpsaMaxValue("300000")]
        public static int MoveOrderingBestMoveBias = 231227;

        [SpsaMinValue("50000"), SpsaMaxValue("100000")]
        public static int MoveOrderingEnPassantMoveBias = 78712;

        [SpsaMinValue("50000"), SpsaMaxValue("200000")]
        public static int MoveOrderingWinningCaptureBias = 147757;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int MoveOrderingLosingCaptureBias = 16289;

        [SpsaMinValue("30000"), SpsaMaxValue("100000")]
        public static int MoveOrderingPromoteBias = 44895;

        [SpsaMinValue("30000"), SpsaMaxValue("100000")]
        public static int MoveOrderingCapturePromoteBias = 34032;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int MoveOrderingKillerABias = 55773;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int MoveOrderingCounterMoveBias = 84751;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int InterestingNegaMaxMoveScore = 38951;

        [SpsaMinValue("10000"), SpsaMaxValue("100000")]
        public static int InterestingQuiescenceMoveScore = 45435;

        [SpsaMinValue("200"), SpsaMaxValue("300"), SpsaIgnore()]
        public static int ProbCutBetaMargin = 220;

        [SpsaMinValue("0"), SpsaMaxValue("6"), SpsaIgnore()]
        public static int ProbCutMinDepth = 3;

        [SpsaMinValue("30"), SpsaMaxValue("100")]
        public static int AsperationWindowA = 38;

        [SpsaMinValue("50"), SpsaMaxValue("200")]
        public static int AsperationWindowB = 55;

        [SpsaMinValue("100"), SpsaMaxValue("500")]
        public static int AsperationWindowC = 278;

        [SpsaMinValue("400"), SpsaMaxValue("1500")]
        public static int AsperationWindowD = 855;

        [SpsaMinValue("1400"), SpsaMaxValue("3000")]
        public static int AsperationWindowE = 2723;

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
                var min = int.Parse(parameter.MinValue);
                var max = int.Parse(parameter.MaxValue);
                var ss = Math.Max(minStepSize, (max - min) / 20.0);
                var lr = double.Round(Math.Max(normalLearningRate, normalLearningRate * (0.50 / ss)), 4);

                return
                    $"{parameter.Name}, {parameter.Type}, {parameter.DefaultValue}, {parameter.MinValue}, {parameter.MaxValue}, {ss}, {lr}";
            }
            else
            {
                var min = double.Parse(parameter.MinValue);
                var max = double.Parse(parameter.MaxValue);
                var ss = Math.Max(minStepSize, (max - min) / 20.0);
                var lr = double.Round(Math.Max(normalLearningRate, normalLearningRate * (0.50 / ss)), 4);

                return
                    $"{parameter.Name}, {parameter.Type}, {parameter.DefaultValue}, {parameter.MinValue}, {parameter.MaxValue}, {ss}, {lr}";
            }
        }

        public static void ProcessUCIOptions()
        {
            TuningParameters = new Dictionary<string, SpsaParameter>();

            foreach (var field in typeof(SpsaOptions).GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(x => !x.IsLiteral).ToList())
            {
                var defaultValue = field.GetValue(null)?.ToString() ?? "0";
                // Retrieve the custom attributes, if present
                var minValueAttribute = field.GetCustomAttribute<SpsaMinValueAttribute>();
                var maxValueAttribute = field.GetCustomAttribute<SpsaMaxValueAttribute>();
                var ignoreAttribute = field.GetCustomAttribute<SpsaIgnoreAttribute>();
                if (ignoreAttribute != null)
                {
                    continue;
                }

                // Set MinValue and MaxValue using the attributes, or default to the default value
                var minValue = minValueAttribute?.MinValue ?? defaultValue;
                var maxValue = maxValueAttribute?.MaxValue ?? defaultValue;

                TuningParameters[field.Name.ToLower()] = new SpsaParameter()
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

        public static void SetParameterValue(string[] tokens)
        {
            var parameter = TuningParameters[tokens[2].ToLower()];
            if (parameter.Type == "int")
            {
                if (tokens[3] == "value" && int.TryParse(tokens[4], out var value))
                {
                    parameter.FieldHandle.SetValue(null, value);
                }
            }
            else
            {
                if (tokens[3] == "value" && float.TryParse(tokens[4], out var value))
                {
                    parameter.FieldHandle.SetValue(null, value);
                }
            }

            ProcessUCIOptions();
        }

        public static bool HasParameter(string parameterName)
        {
            return TuningParameters.ContainsKey(parameterName);
        }
    }

}