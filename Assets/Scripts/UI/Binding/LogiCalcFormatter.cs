#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NCalc;
using NCalc.Factories;
using SmartFormat.Core.Extensions;
using SmartFormat.Core.Parsing;

namespace SmartFormat.Extensions
{
    /// <summary>
    /// An <see cref="IFormatter"/> used to evaluate mathematical and logical expressions.
    /// Uses NCalc (https://github.com/ncalc/ncalc) for expression evaluation.
    /// </summary>
    /// <example>
    /// Template: "Result: {score:calc: [score] * 2}" where score=21 → "Result: 42"
    /// Template: "Total: {:calc(0.00):({count} + {bonus}) * 1.2}".
    /// </example>
    /// <remarks>
    /// The formatter name is "calc".
    /// Inside the expression, use SmartFormat placeholders like {name}
    /// to reference values from the current data source.
    /// Placeholders like {name} become NCalc parameters [name].
    /// Supports all NCalc arithmetic, logical, and function operators.
    /// </remarks>
    public class LogiCalcFormatter : IFormatter
    {
        /// <inheritdoc/>
        public string Name { get; set; } = "calc";

        /// <inheritdoc/>
        public bool CanAutoDetect { get; set; } = false;

        /// <inheritdoc/>
        public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
        {
            var format = formattingInfo.Format;
            if (format == null)
            {
                return false;
            }

            // Build the NCalc expression string and collect parameter values
            var parameters = new Dictionary<string, object?>();
            var exprBuilder = new StringBuilder();

            foreach (var item in format.Items)
            {
                if (item is LiteralText literalItem)
                {
                    exprBuilder.Append(literalItem.AsSpan());
                    continue;
                }

                // Otherwise, the item must be a placeholder
                var placeholder = (Placeholder)item;
                var paramName = GetSelectorName(placeholder);

                if (paramName.Length > 0)
                {
                    // Resolve the placeholder value from the current data source
                    if (formattingInfo.CurrentValue is IReadOnlyDictionary<string, object> dict)
                    {
                        dict.TryGetValue(paramName, out var paramValue);

                        // Try to convert strings to numbers for NCalc arithmetic
                        if (paramValue is string strVal)
                        {
                            paramValue = ConvertStringToNumeric(strVal) ?? paramValue;
                        }

                        parameters[paramName] = paramValue;
                    }

                    // Append as NCalc parameter reference
                    exprBuilder.Append('[');
                    exprBuilder.Append(paramName);
                    exprBuilder.Append(']');
                }
            }

            var expressionStr = exprBuilder.ToString();
            if (string.IsNullOrEmpty(expressionStr))
            {
                return false;
            }

            try
            {
                // Parse and evaluate the NCalc expression
                const ExpressionOptions options = ExpressionOptions.NoCache;
                var context = new ExpressionContext(options, CultureInfo.InvariantCulture);
                var logExpr = LogicalExpressionFactory.Create(expressionStr, context);
                var nCalcExpr = new Expression(logExpr);

                foreach (var kvp in parameters)
                {
                    nCalcExpr.Parameters[kvp.Key] = kvp.Value;
                }

                var result = nCalcExpr.Evaluate();

                // Format the result using the formatter options (e.g. "0.00")
                if (result == null)
                {
                    formattingInfo.Write(string.Empty);
                }
                else if (formattingInfo.FormatterOptions.Length > 0)
                {
                    var formatted = formattingInfo.FormatDetails.Formatter.Format(
                        "{0:" + formattingInfo.FormatterOptions + "}",
                        new[] { result });
                    formattingInfo.Write(formatted);
                }
                else
                {
                    formattingInfo.Write(result.ToString() ?? string.Empty);
                }

                return true;
            }
            catch
            {
                // Leave the template text unchanged if evaluation fails
                return false;
            }
        }

        /// <summary>
        /// Builds the dot-separated selector name from a placeholder.
        /// Example: "{Person.Siblings[0]}" => "Person.Siblings.0".
        /// </summary>
        private static string GetSelectorName(Placeholder placeholder)
        {
            var nameBuilder = new StringBuilder();
            var first = true;

            foreach (var selector in placeholder.GetSelectors())
            {
                if (selector.Length == 0 || selector.Operator == ",")
                {
                    continue;
                }

                if (!first)
                {
                    nameBuilder.Append('.');
                }

                nameBuilder.Append(selector.AsSpan());
                first = false;
            }

            return nameBuilder.ToString();
        }

        /// <summary>
        /// Attempts to convert a string to a numeric type (int, long, float, double)
        /// for use in NCalc arithmetic.
        /// </summary>
        private static object? ConvertStringToNumeric(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            {
                return intVal;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            {
                return longVal;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
            {
                return floatVal;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
            {
                return doubleVal;
            }

            return null;
        }
    }
}
