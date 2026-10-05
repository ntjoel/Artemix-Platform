using Cognex.VisionPro.ToolBlock;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Reads an optional classification result from the root outputs of a camera ToolBlock.
    /// Missing outputs mean that the job has no classification feature and are not an error.
    /// </summary>
    public static class VisionClassificationResultReader
    {
        private static readonly string[] ClassOutputAliases =
        {
            "EL_Classify",
            "Classify",
            "Classification",
            "Class"
        };

        private static readonly string[] ScoreOutputAliases =
        {
            "EL_Score",
            "Score",
            "ClassificationScore",
            "Confidence"
        };

        private static readonly HashSet<string> PassingLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OK",
            "GOOD",
            "PASS",
            "PASSED",
            "COMPLIANT"
        };

        private static readonly HashSet<string> FailingLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NOK",
            "NG",
            "KO",
            "FAIL",
            "FAILED",
            "BAD",
            "REJECT",
            "NOREAD"
        };

        public static CameraClassificationResult Read(CogToolBlock toolBlock, bool? validationPassed = null)
        {
            if (toolBlock?.Outputs == null)
            {
                return null;
            }

            string classOutputName = FindOutputName(toolBlock, ClassOutputAliases);
            if (string.IsNullOrWhiteSpace(classOutputName) ||
                !TryReadOutput(toolBlock, classOutputName, out object rawClass))
            {
                return null;
            }

            string className = Convert.ToString(rawClass, CultureInfo.CurrentCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(className))
            {
                return null;
            }

            string scoreOutputName = FindOutputName(toolBlock, ScoreOutputAliases);
            double? score = null;
            if (!string.IsNullOrWhiteSpace(scoreOutputName) &&
                TryReadOutput(toolBlock, scoreOutputName, out object rawScore) &&
                TryConvertToDouble(rawScore, out double parsedScore))
            {
                score = parsedScore;
            }

            return new CameraClassificationResult
            {
                ClassName = className,
                Score = score,
                ClassOutputName = classOutputName,
                ScoreOutputName = scoreOutputName,
                State = validationPassed.HasValue
                    ? (validationPassed.Value
                        ? CameraClassificationState.Passed
                        : CameraClassificationState.Failed)
                    : ResolveState(className)
            };
        }

        /// <summary>
        /// Returns true when the ToolBlock exposes at least one supported class
        /// output. Score is optional: a job may still classify correctly while
        /// omitting confidence from its public outputs.
        /// </summary>
        public static bool HasClassificationOutput(CogToolBlock toolBlock)
        {
            return toolBlock?.Outputs != null &&
                   !string.IsNullOrWhiteSpace(FindOutputName(toolBlock, ClassOutputAliases));
        }

        public static bool IsPassingLabel(string className)
        {
            return !string.IsNullOrWhiteSpace(className) && PassingLabels.Contains(className.Trim());
        }

        public static bool IsFailingLabel(string className)
        {
            return !string.IsNullOrWhiteSpace(className) && FailingLabels.Contains(className.Trim());
        }

        private static CameraClassificationState ResolveState(string className)
        {
            if (IsPassingLabel(className))
            {
                return CameraClassificationState.Passed;
            }

            if (IsFailingLabel(className))
            {
                return CameraClassificationState.Failed;
            }

            return CameraClassificationState.Informational;
        }

        private static string FindOutputName(CogToolBlock toolBlock, IEnumerable<string> aliases)
        {
            foreach (string alias in aliases)
            {
                for (int index = 0; index < toolBlock.Outputs.Count; index++)
                {
                    string outputName = toolBlock.Outputs[index]?.Name;
                    if (string.Equals(outputName, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        return outputName;
                    }
                }
            }

            return null;
        }

        private static bool TryReadOutput(CogToolBlock toolBlock, string outputName, out object value)
        {
            value = null;
            try
            {
                if (toolBlock?.Outputs == null ||
                    string.IsNullOrWhiteSpace(outputName) ||
                    !toolBlock.Outputs.Contains(outputName))
                {
                    return false;
                }

                value = toolBlock.Outputs[outputName]?.Value;
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value == null)
            {
                return false;
            }

            if (value is string text)
            {
                bool parsed = double.TryParse(
                                  text,
                                  NumberStyles.Float | NumberStyles.AllowThousands,
                                  CultureInfo.CurrentCulture,
                                  out result) ||
                              double.TryParse(
                                  text,
                                  NumberStyles.Float | NumberStyles.AllowThousands,
                                  CultureInfo.InvariantCulture,
                                  out result);
                return parsed && !double.IsNaN(result) && !double.IsInfinity(result);
            }

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch
            {
                return false;
            }
        }
    }
}
