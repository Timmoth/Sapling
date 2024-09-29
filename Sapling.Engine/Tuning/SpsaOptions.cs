using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

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
        [SpsaMinValue("10"), SpsaMaxValue("100")]
        public static int ReverseFutilityPruningMargin = 75;

        [SpsaMinValue("1"), SpsaMaxValue("10")]
        public static int ReverseFutilityPruningDepth = 7;

        [SpsaMinValue("1"), SpsaMaxValue("10")]
        public static int NullMovePruningDepth = 2;

        [SpsaMinValue("1"), SpsaMaxValue("10")]
        public static int NullMovePruningReductionA = 3;

        [SpsaMinValue("1"), SpsaMaxValue("10")]
        public static int NullMovePruningReductionB = 4;

        [SpsaMinValueAttribute("1"), SpsaMaxValue("10")]
        public static int NullMovePruningReductionC = 3;

        [SpsaMinValueAttribute("10"), SpsaMaxValue("400")]
        public static int RazorMarginA = 125;

        [SpsaMinValueAttribute("10"), SpsaMaxValue("400")]
        public static int RazorMarginB = 300;

        [SpsaMinValueAttribute("1"), SpsaMaxValue("10")]
        public static int InternalIterativeDeepeningDepth = 2;

        [SpsaMinValueAttribute("1"), SpsaMaxValue("10")]
        public static int LateMovePruningConstant = 8;

        [SpsaMinValueAttribute("1"), SpsaMaxValue("10")]
        public static int LateMoveReductionMinDepth = 3;

        [SpsaMinValueAttribute("1"), SpsaMaxValue("10")]
        public static int LateMoveReductionMinMoves = 2;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionInterestingA = 0.2f;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionInterestingB = 3.3f;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionA = 1.35f;

        [SpsaMinValueAttribute("0.1"), SpsaMaxValue("10")]
        public static float LateMoveReductionB = 2.75f;

        [SpsaMinValueAttribute("7000"), SpsaMaxValue("10000")]
        public static int HistoryHeuristicMaxHistory = 8192;

        [SpsaMinValueAttribute("500"), SpsaMaxValue("1000")]
        public static int HistoryHeuristicBonusMax = 640;

        [SpsaMinValueAttribute("10"), SpsaMaxValue("100")]
        public static int HistoryHeuristicBonusCoeff = 80;

        [SpsaMinValueAttribute("500000"), SpsaMaxValue("10000000")]
        public static int MoveOrderingWinningCaptureBias = 10_000_000;

        [SpsaMinValueAttribute("10000"), SpsaMaxValue("30000")]
        public static int MoveOrderingLosingCaptureBias = 16_000;

        [SpsaMinValueAttribute("70000"), SpsaMaxValue("700000")]
        public static int MoveOrderingPromoteBias = 600_000;

        [SpsaMinValueAttribute("70000"), SpsaMaxValue("600000")]
        public static int MoveOrderingKillerABias = 500_000;

        [SpsaMinValueAttribute("70000"), SpsaMaxValue("300000")]
        public static int MoveOrderingKillerBBias = 250_000;

        [SpsaMinValueAttribute("20000"), SpsaMaxValue("80000")]
        public static int MoveOrderingCounterMoveBias = 65_000;

        [SpsaMinValueAttribute("10000"), SpsaMaxValue("30000")]
        public static int InterestingNegaMaxMoveScore = 16_000;

        [SpsaMinValueAttribute("10000"), SpsaMaxValue("30000")]
        public static int InterestingQuiescenceMoveScore = 16_000;
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
            const double normalLearningRate = 0.002;

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
