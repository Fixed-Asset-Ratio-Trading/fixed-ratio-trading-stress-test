using System;
using System.Globalization;

namespace FixedRatioStressTest.Common.Utils
{
    /// <summary>
    /// Fixed Ratio Trading (FRT) token calculation utility.
    /// Provides exact output calculations for token swaps based on fixed ratios.
    /// </summary>
    public static class FRTExpectedTokens
    {
        /// <summary>
        /// Calculates the exact expected output tokens for a Fixed Ratio Trading swap.
        /// This function matches the on-chain contract calculation exactly.
        /// </summary>
        /// <param name="valueIn">The input amount in basis points (smallest unit)</param>
        /// <param name="tokenADecimals">Decimals for Token A</param>
        /// <param name="tokenBDecimals">Decimals for Token B</param>
        /// <param name="tokenARatio">Token A ratio value (basis points)</param>
        /// <param name="tokenBRatio">Token B ratio value (basis points)</param>
        /// <param name="aToB">True for A→B swap, false for B→A swap</param>
        /// <returns>The exact output amount in basis points</returns>
        public static ulong Calculate(
            ulong valueIn,
            int tokenADecimals,
            int tokenBDecimals,
            ulong tokenARatio,
            ulong tokenBRatio,
            bool aToB)
        {
            // The Fixed Ratio Trading contract uses this exact formula:
            // For A→B: output = (input × tokenBRatio) ÷ tokenARatio
            // For B→A: output = (input × tokenARatio) ÷ tokenBRatio
            
            // IMPORTANT: The contract does integer division which truncates any remainder.
            // This means fractional amounts below the output token's smallest unit are discarded.
            
            if (aToB)
            {
                // A→B swap: output_B = input_A × ratio_B ÷ ratio_A
                // Example: 1 SOL (1e9) at 1:160 ratio = 1e9 × 160e6 ÷ 1e9 = 160e6 USDC
                return checked((valueIn * tokenBRatio) / tokenARatio);
            }
            else
            {
                // B→A swap: output_A = input_B × ratio_A ÷ ratio_B
                // Example: 160 USDC (160e6) at 1:160 ratio = 160e6 × 1e9 ÷ 160e6 = 1e9 SOL
                return checked((valueIn * tokenARatio) / tokenBRatio);
            }
        }

        /// <summary>
        /// Validates if the input amount will produce a non-zero output after rounding.
        /// Useful for preventing dust trades that result in zero output.
        /// </summary>
        public static bool WillProduceOutput(
            ulong valueIn,
            int tokenADecimals,
            int tokenBDecimals,
            ulong tokenARatio,
            ulong tokenBRatio,
            bool aToB)
        {
            var output = Calculate(valueIn, tokenADecimals, tokenBDecimals, tokenARatio, tokenBRatio, aToB);
            return output > 0;
        }

        /// <summary>
        /// Calculates the minimum input required to produce at least 1 unit of output.
        /// Useful for avoiding dust trades.
        /// </summary>
        public static ulong MinimumInputForOutput(
            int tokenADecimals,
            int tokenBDecimals,
            ulong tokenARatio,
            ulong tokenBRatio,
            bool aToB)
        {
            if (aToB)
            {
                // For A→B: min_input_A = ceiling(1 × ratio_A ÷ ratio_B)
                // We want at least 1 basis point of B, so solve for A:
                // 1 = input_A × ratio_B ÷ ratio_A
                // input_A = ratio_A ÷ ratio_B (rounded up)
                return (tokenARatio + tokenBRatio - 1) / tokenBRatio;
            }
            else
            {
                // For B→A: min_input_B = ceiling(1 × ratio_B ÷ ratio_A)
                // We want at least 1 basis point of A, so solve for B:
                // 1 = input_B × ratio_A ÷ ratio_B
                // input_B = ratio_B ÷ ratio_A (rounded up)
                return (tokenBRatio + tokenARatio - 1) / tokenARatio;
            }
        }

        /// <summary>
        /// Provides a human-readable explanation of the calculation.
        /// Useful for debugging and logging.
        /// </summary>
        public static string ExplainCalculation(
            ulong valueIn,
            int tokenADecimals,
            int tokenBDecimals,
            ulong tokenARatio,
            ulong tokenBRatio,
            bool aToB)
        {
            var output = Calculate(valueIn, tokenADecimals, tokenBDecimals, tokenARatio, tokenBRatio, aToB);
            
            if (aToB)
            {
                return $"A→B Swap: {valueIn} × {tokenBRatio} ÷ {tokenARatio} = {output} " +
                       $"(Input: {FormatAmount(valueIn, tokenADecimals)} Token A, " +
                       $"Output: {FormatAmount(output, tokenBDecimals)} Token B)";
            }
            else
            {
                return $"B→A Swap: {valueIn} × {tokenARatio} ÷ {tokenBRatio} = {output} " +
                       $"(Input: {FormatAmount(valueIn, tokenBDecimals)} Token B, " +
                       $"Output: {FormatAmount(output, tokenADecimals)} Token A)";
            }
        }

        private static string FormatAmount(ulong basisPoints, int decimals)
        {
            if (decimals == 0)
                return basisPoints.ToString(CultureInfo.InvariantCulture);

            var value = (double)basisPoints / Math.Pow(10, decimals);
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
