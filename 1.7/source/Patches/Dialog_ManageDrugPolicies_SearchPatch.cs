using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Drug_Policy_Search.Patches
{
    // Vanilla DoPolicyConfigArea is kept intact. Each infix below wraps one operation inside it:
    // the header height constant, the DoColumnLabels call (draws the search bar), the
    // DrugPolicy.Count / DrugPolicy[int] reads (filtered view of the rows) and the empty-list label.
    [HarmonyPatch(typeof(Dialog_ManageDrugPolicies), "DoPolicyConfigArea")]
    public static class Dialog_ManageDrugPolicies_SearchPatch
    {
        internal const float SearchHeaderHeight = 64f;
        private const float VanillaHeaderHeight = 36f;
        private const float ColumnsHeight = 36f;
        private const float SearchWidgetHeight = 24f;
        private const float SearchTopPadding = 4f;
        private const float SearchSidePadding = 24f;
        private const float SearchMinLeftOffset = 140f;
        private const float SearchLeftOffsetFactor = 0.2f;
        private const float SearchMaxWidth = 520f;
        private const float SearchMinWidth = 220f;

        private static readonly ConditionalWeakTable<Dialog_ManageDrugPolicies, SearchState> SearchStates =
            new ConditionalWeakTable<Dialog_ManageDrugPolicies, SearchState>();

        // Vanilla `rect2.height = 36f`: the header rect now holds the search bar above the column labels.
        [HarmonyPostfix]
        [HarmonyInfix(VanillaHeaderHeight)]
        public static float HeaderHeight_Postfix(float __result)
        {
            return SearchHeaderHeight;
        }

        // Vanilla `DoColumnLabels(rect2)`: draw the search bar in the top of the header and
        // shrink the rect handed to the labels back to their original bottom strip.
        [HarmonyPrefix]
        [HarmonyInfix(typeof(Dialog_ManageDrugPolicies), "DoColumnLabels", typeof(Rect))]
        public static void DoColumnLabels_Prefix(
            ref Rect rect,
            [HarmonyOuter] Dialog_ManageDrugPolicies __instance,
            [HarmonyOuter] DrugPolicy ___policyInt,
            [HarmonyOuter] ref Vector2 ___scrollPosition)
        {
            SearchState state = GetState(__instance);
            Rect headerRect = rect;
            rect.yMin = headerRect.yMax - ColumnsHeight;

            UpdateSearchFeedback(___policyInt, state);
            bool filterChanged = false;
            state.SearchWidget.OnGUI(GetSearchRect(headerRect), () => filterChanged = true);
            if (filterChanged)
            {
                ___scrollPosition = Vector2.zero;
                state.CacheDirty = true;
                UpdateSearchFeedback(___policyInt, state);
            }
        }

        // Vanilla `selectedPolicy.Count` (empty check, scroll view height, loop bound).
        [HarmonyPostfix]
        [HarmonyInfix(typeof(DrugPolicy), "get_Count")]
        public static int PolicyCount_Postfix(
            int __result,
            [HarmonyOuter] Dialog_ManageDrugPolicies __instance,
            [HarmonyOuter] DrugPolicy ___policyInt)
        {
            SearchState state = GetState(__instance);
            if (!state.SearchWidget.filter.Active)
            {
                return __result;
            }

            return GetVisibleEntries(___policyInt, state).Count;
        }

        // Vanilla `selectedPolicy[i]` handed to DoEntryRow: row i is the i-th visible entry.
        [HarmonyPostfix]
        [HarmonyInfix(typeof(DrugPolicy), "get_Item", typeof(int))]
        public static DrugPolicyEntry PolicyEntry_Postfix(
            DrugPolicyEntry __result,
            int index,
            [HarmonyOuter] Dialog_ManageDrugPolicies __instance,
            [HarmonyOuter] DrugPolicy ___policyInt)
        {
            SearchState state = GetState(__instance);
            if (!state.SearchWidget.filter.Active)
            {
                return __result;
            }

            return GetVisibleEntries(___policyInt, state)[index];
        }

        // Vanilla `"NoDrugs".Translate()`: the only label drawn when the list is empty.
        [HarmonyPostfix]
        [HarmonyInfix(typeof(Translator), "Translate", typeof(string))]
        public static TaggedString EmptyLabel_Postfix(
            TaggedString __result,
            string key,
            [HarmonyOuter] Dialog_ManageDrugPolicies __instance)
        {
            SearchState state = GetState(__instance);
            if (state.SearchWidget.filter.Active)
            {
                return "DrugPolicySearch.NoMatches".Translate(state.SearchWidget.filter.Text);
            }

            // Core 1.7 only ships NoDrugsBrackets; fall back so the label never shows a raw key.
            return key.CanTranslate() ? __result : "NoDrugsBrackets".Translate();
        }

        private static SearchState GetState(Dialog_ManageDrugPolicies dialog)
        {
            return SearchStates.GetValue(dialog, _ => new SearchState());
        }

        private static Rect GetSearchRect(Rect headerRect)
        {
            float leftOffset = Mathf.Min(SearchMinLeftOffset, headerRect.width * SearchLeftOffsetFactor);
            float width = Mathf.Clamp(headerRect.width - leftOffset - SearchSidePadding, SearchMinWidth, SearchMaxWidth);
            float x = headerRect.x + leftOffset;
            if (x + width > headerRect.xMax - SearchSidePadding)
            {
                width = Mathf.Max(SearchMinWidth, headerRect.xMax - SearchSidePadding - x);
            }

            return new Rect(x, headerRect.y + SearchTopPadding, width, SearchWidgetHeight);
        }

        private static List<DrugPolicyEntry> GetVisibleEntries(DrugPolicy selectedPolicy, SearchState state)
        {
            string filterText = state.SearchWidget.filter.Text;
            int policyCount = selectedPolicy?.Count ?? 0;
            if (!state.CacheDirty && state.CachedPolicy == selectedPolicy && state.CachedPolicyCount == policyCount && state.CachedFilterText == filterText)
            {
                return state.VisibleEntries;
            }

            state.VisibleEntries.Clear();
            state.CachedPolicy = selectedPolicy;
            state.CachedPolicyCount = policyCount;
            state.CachedFilterText = filterText;
            state.CacheDirty = false;

            if (selectedPolicy == null)
            {
                return state.VisibleEntries;
            }

            for (int i = 0; i < policyCount; i++)
            {
                DrugPolicyEntry entry = selectedPolicy[i];
                if (state.SearchWidget.filter.Matches(entry.drug))
                {
                    state.VisibleEntries.Add(entry);
                }
            }

            return state.VisibleEntries;
        }

        private static void UpdateSearchFeedback(DrugPolicy selectedPolicy, SearchState state)
        {
            if (selectedPolicy == null || !state.SearchWidget.filter.Active)
            {
                state.SearchWidget.noResultsMatched = false;
                return;
            }

            state.SearchWidget.noResultsMatched = GetVisibleEntries(selectedPolicy, state).Count == 0;
        }

        private sealed class SearchState
        {
            public readonly QuickSearchWidget SearchWidget = new QuickSearchWidget();

            public readonly List<DrugPolicyEntry> VisibleEntries = new List<DrugPolicyEntry>();

            public DrugPolicy CachedPolicy;

            public string CachedFilterText = string.Empty;

            public int CachedPolicyCount = -1;

            public bool CacheDirty = true;
        }
    }

    // Dialog_ManagePolicies.DoWindowContents reserves OffsetHeaderY above the policy list and config area.
    // Vanilla returns 36f; make room for the search bar as well.
    [HarmonyPatch(typeof(Dialog_ManageDrugPolicies), "OffsetHeaderY", MethodType.Getter)]
    public static class Dialog_ManageDrugPolicies_OffsetHeaderY_SearchPatch
    {
        [HarmonyInfix(36f)]
        public static float Postfix(float __result)
        {
            return Dialog_ManageDrugPolicies_SearchPatch.SearchHeaderHeight;
        }
    }
}
