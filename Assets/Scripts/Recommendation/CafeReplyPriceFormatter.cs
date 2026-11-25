using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VirtualCafe.Recommendation
{
    // Model explanations are retained, but customer-facing price statements come from the menu service.
    public static class CafeReplyPriceFormatter
    {
        private static readonly Regex PriceClause = new Regex(
            @"\b(price|prices|cost|costs|total|yuan|yen|dollars?|euros?|CNY|RMB|USD|JPY)\b|[¥￥$€£]|价格|售价|价钱|合计|总价|(?:[零一二三四五六七八九十百千万两〇]+|[0-9][0-9,.]*)\s*(?:元|块|円)",
            RegexOptions.IgnoreCase);
        public static string Compose(string reply, string authoritativeSummary)
        {
            var kept = new List<string>();
            foreach (string clause in Regex.Split(reply ?? "", @"(?<=[。！？!?;；\n,，、])|\.(?=\s|$)"))
            {
                string text = clause.Trim();
                if (text.Length > 0 && !PriceClause.IsMatch(text)) kept.Add(text);
            }
            string explanation = string.Join(" ", kept).Trim(' ', ',', '，', '、', ';', '；');
            if (string.IsNullOrWhiteSpace(authoritativeSummary))
                return explanation.Length > 0 ? explanation : CafeUiLocalization.Text("Tell me more about your preferences, or browse the menu.");
            return explanation.Length > 0 ? explanation + "\n\n" + authoritativeSummary : authoritativeSummary;
        }
    }
}
