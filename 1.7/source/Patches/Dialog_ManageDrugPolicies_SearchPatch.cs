using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Drug_Policy_Search.Filters;
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
        internal const float SearchHeaderHeight = 96f;
        private const float VanillaHeaderHeight = 36f;
        private const float ColumnsHeight = 36f;
        private const float SearchWidgetHeight = 24f;
        private const float SearchTopPadding = 4f;
        private const float SearchSidePadding = 24f;
        private const float SearchMinLeftOffset = 140f;
        private const float SearchLeftOffsetFactor = 0.2f;
        private const float SearchMaxWidth = 520f;
        private const float SearchMinWidth = 220f;
        private const float FilterRowTop = 32f;
        private const float FilterHeight = 24f;
        private const float FilterGap = 4f;

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

            DrawFilterButtons(headerRect, ___policyInt, state, __instance);
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
            if (!state.HasActiveFilter)
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
            if (!state.HasActiveFilter)
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
            if (state.HasActiveFilter)
            {
                return state.SearchWidget.filter.Active && !state.HasSelectedDropdown
                    ? "DrugPolicySearch.NoMatches".Translate(state.SearchWidget.filter.Text)
                    : "DrugPolicySearch.NoFilterMatches".Translate();
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

        private static void DrawFilterButtons(Rect headerRect, DrugPolicy policy, SearchState state,
            Dialog_ManageDrugPolicies dialog)
        {
            float leftOffset = Mathf.Min(SearchMinLeftOffset, headerRect.width * SearchLeftOffsetFactor);
            float availableWidth = headerRect.width - leftOffset - SearchSidePadding;
            float buttonWidth = (availableWidth - FilterGap * 2f) / 3f;
            float x = headerRect.x + leftOffset;
            float y = headerRect.y + FilterRowTop;

            Rect useRect = new Rect(x, y, buttonWidth, FilterHeight);
            Rect techRect = new Rect(x + buttonWidth + FilterGap, y, buttonWidth, FilterHeight);
            Rect categoryRect = new Rect(techRect.xMax + FilterGap, y, buttonWidth, FilterHeight);

            if (state.LabelsDirty)
            {
                string all = "DrugPolicySearch.All".Translate();
                state.UseButtonLabel = "DrugPolicySearch.Use".Translate() + ": " +
                    (state.UseMethod.HasValue ? GetUseLabel(state.UseMethod.Value) : all);
                state.TechButtonLabel = "DrugPolicySearch.Tech".Translate() + ": " +
                    (state.TechLevel.HasValue ? GetTechLabel(state.TechLevel.Value) : all);
                state.CategoryButtonLabel = "DrugPolicySearch.Category".Translate() + ": " +
                    (state.Category.HasValue ? GetCategoryLabel(state.Category.Value) : all);
                state.UseTooltip = "DrugPolicySearch.UseTooltip".Translate();
                state.TechTooltip = "DrugPolicySearch.TechTooltip".Translate();
                state.CategoryTooltip = "DrugPolicySearch.CategoryTooltip".Translate();
                state.LabelsDirty = false;
            }

            if (Widgets.ButtonText(useRect, state.UseButtonLabel.Truncate(useRect.width - 12f)))
            {
                ShowUseMenu(policy, state, dialog);
            }
            if (Widgets.ButtonText(techRect, state.TechButtonLabel.Truncate(techRect.width - 12f)))
            {
                ShowTechMenu(policy, state, dialog);
            }
            if (Widgets.ButtonText(categoryRect, state.CategoryButtonLabel.Truncate(categoryRect.width - 12f)))
            {
                ShowCategoryMenu(policy, state, dialog);
            }

            TooltipHandler.TipRegion(useRect, state.UseTooltip);
            TooltipHandler.TipRegion(techRect, state.TechTooltip);
            TooltipHandler.TipRegion(categoryRect, state.CategoryTooltip);
        }

        private static void ShowUseMenu(DrugPolicy policy, SearchState state, Dialog_ManageDrugPolicies dialog)
        {
            var choices = new HashSet<DrugUseMethod>();
            for (int i = 0; policy != null && i < policy.Count; i++)
            {
                choices.Add(DrugFilterUtility.GetUseMethod(policy[i].drug));
            }
            if (state.UseMethod.HasValue)
            {
                choices.Add(state.UseMethod.Value);
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("DrugPolicySearch.All".Translate(), () => SetUseMethod(dialog, null))
            };
            foreach (DrugUseMethod method in (DrugUseMethod[])System.Enum.GetValues(typeof(DrugUseMethod)))
            {
                if (choices.Contains(method))
                {
                    DrugUseMethod choice = method;
                    options.Add(new FloatMenuOption(GetUseLabel(choice), () => SetUseMethod(dialog, choice)));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowTechMenu(DrugPolicy policy, SearchState state, Dialog_ManageDrugPolicies dialog)
        {
            var choices = new HashSet<TechLevel>();
            for (int i = 0; policy != null && i < policy.Count; i++)
            {
                choices.Add(policy[i].drug.techLevel);
            }
            if (state.TechLevel.HasValue)
            {
                choices.Add(state.TechLevel.Value);
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("DrugPolicySearch.All".Translate(), () => SetTechLevel(dialog, null))
            };
            var ordered = new List<TechLevel>(choices);
            ordered.Sort();
            foreach (TechLevel level in ordered)
            {
                TechLevel choice = level;
                options.Add(new FloatMenuOption(GetTechLabel(choice), () => SetTechLevel(dialog, choice)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowCategoryMenu(DrugPolicy policy, SearchState state, Dialog_ManageDrugPolicies dialog)
        {
            var choices = new HashSet<DrugCategory>();
            for (int i = 0; policy != null && i < policy.Count; i++)
            {
                choices.Add(policy[i].drug.ingestible?.drugCategory ?? DrugCategory.None);
            }
            if (state.Category.HasValue)
            {
                choices.Add(state.Category.Value);
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("DrugPolicySearch.All".Translate(), () => SetCategory(dialog, null))
            };
            var ordered = new List<DrugCategory>(choices);
            ordered.Sort();
            foreach (DrugCategory category in ordered)
            {
                DrugCategory choice = category;
                options.Add(new FloatMenuOption(GetCategoryLabel(choice), () => SetCategory(dialog, choice)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string GetUseLabel(DrugUseMethod method)
        {
            return (string)("DrugPolicySearch.Use" + method).Translate();
        }

        private static string GetTechLabel(TechLevel level)
        {
            return (level == TechLevel.Undefined
                ? (string)"DrugPolicySearch.Unspecified".Translate()
                : level.ToStringHuman()).CapitalizeFirst();
        }

        private static string GetCategoryLabel(DrugCategory category)
        {
            return category.GetLabel().CapitalizeFirst();
        }

        private static void SetUseMethod(Dialog_ManageDrugPolicies dialog, DrugUseMethod? method)
        {
            SearchState state = GetState(dialog);
            if (state.UseMethod != method)
            {
                state.UseMethod = method;
                FilterChanged(dialog, state);
            }
        }

        private static void SetTechLevel(Dialog_ManageDrugPolicies dialog, TechLevel? level)
        {
            SearchState state = GetState(dialog);
            if (state.TechLevel != level)
            {
                state.TechLevel = level;
                FilterChanged(dialog, state);
            }
        }

        private static void SetCategory(Dialog_ManageDrugPolicies dialog, DrugCategory? category)
        {
            SearchState state = GetState(dialog);
            if (state.Category != category)
            {
                state.Category = category;
                FilterChanged(dialog, state);
            }
        }

        private static void FilterChanged(Dialog_ManageDrugPolicies dialog, SearchState state)
        {
            state.CacheDirty = true;
            state.LabelsDirty = true;
            // Float menu actions run outside DoColumnLabels, so the private scroll field is reset here.
            AccessTools.Field(typeof(Dialog_ManageDrugPolicies), "scrollPosition").SetValue(dialog, Vector2.zero);
        }

        private static List<DrugPolicyEntry> GetVisibleEntries(DrugPolicy selectedPolicy, SearchState state)
        {
            string filterText = state.SearchWidget.filter.Text;
            int policyCount = selectedPolicy?.Count ?? 0;
            if (!state.CacheDirty && state.CachedPolicy == selectedPolicy && state.CachedPolicyCount == policyCount &&
                state.CachedFilterText == filterText && state.CachedUseMethod == state.UseMethod &&
                state.CachedTechLevel == state.TechLevel && state.CachedCategory == state.Category)
            {
                return state.VisibleEntries;
            }

            state.VisibleEntries.Clear();
            state.CachedPolicy = selectedPolicy;
            state.CachedPolicyCount = policyCount;
            state.CachedFilterText = filterText;
            state.CachedUseMethod = state.UseMethod;
            state.CachedTechLevel = state.TechLevel;
            state.CachedCategory = state.Category;
            state.CacheDirty = false;

            if (selectedPolicy == null)
            {
                return state.VisibleEntries;
            }

            for (int i = 0; i < policyCount; i++)
            {
                DrugPolicyEntry entry = selectedPolicy[i];
                if (DrugFilterUtility.Matches(entry, state.UseMethod, state.TechLevel, state.Category) &&
                    (!state.SearchWidget.filter.Active || state.SearchWidget.filter.Matches(entry.drug)))
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

            public DrugUseMethod? UseMethod;

            public TechLevel? TechLevel;

            public DrugCategory? Category;

            public DrugUseMethod? CachedUseMethod;

            public TechLevel? CachedTechLevel;

            public DrugCategory? CachedCategory;

            public bool HasSelectedDropdown => UseMethod.HasValue || TechLevel.HasValue || Category.HasValue;

            public bool HasActiveFilter => SearchWidget.filter.Active || HasSelectedDropdown;

            public bool CacheDirty = true;

            public bool LabelsDirty = true;

            public string UseButtonLabel;

            public string TechButtonLabel;

            public string CategoryButtonLabel;

            public string UseTooltip;

            public string TechTooltip;

            public string CategoryTooltip;
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
