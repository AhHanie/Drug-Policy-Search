using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Drug_Policy_Search.Patches
{
    [HarmonyPatch(typeof(Dialog_ManageDrugPolicies), "DoPolicyConfigArea")]
    public static class Dialog_ManageDrugPolicies_SearchPatch
    {
        private const float SearchHeaderHeight = 64f;
        private const float ColumnsHeight = 36f;
        private const float SearchWidgetHeight = 24f;
        private const float SearchTopPadding = 4f;
        private const float SearchSidePadding = 24f;
        private const float SearchMinLeftOffset = 140f;
        private const float SearchLeftOffsetFactor = 0.2f;
        private const float SearchMaxWidth = 520f;
        private const float SearchMinWidth = 220f;
        private const float DrugEntryRowHeight = 35f;

        private static readonly ConditionalWeakTable<Dialog_ManageDrugPolicies, SearchState> SearchStates =
            new ConditionalWeakTable<Dialog_ManageDrugPolicies, SearchState>();

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> patchedInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(Dialog_ManageDrugPolicies_SearchPatch), nameof(DrawPolicyConfigAreaWithSearch))),
                new CodeInstruction(OpCodes.Ret)
            };

            Logger.Message("Transpiler succeeded: Dialog_ManageDrugPolicies.DoPolicyConfigArea");
            return patchedInstructions;
        }

        public static void DrawPolicyConfigAreaWithSearch(Dialog_ManageDrugPolicies dialog, Rect rect)
        {
            SearchState state = SearchStates.GetValue(dialog, _ => new SearchState());
            DrugPolicy selectedPolicy = GetSelectedPolicy(dialog);

            Rect headerRect = rect;
            headerRect.y -= SearchHeaderHeight;
            headerRect.height = SearchHeaderHeight;

            Rect searchRect = GetSearchRect(headerRect);
            Rect columnsRect = headerRect;
            columnsRect.y = headerRect.yMax - ColumnsHeight;
            columnsRect.height = ColumnsHeight;

            UpdateSearchFeedback(selectedPolicy, state);
            state.SearchWidget.OnGUI(searchRect, () => OnSearchChanged(dialog, selectedPolicy, state), () => OnSearchChanged(dialog, selectedPolicy, state));

            Rect contentRect = rect;
            contentRect.yMin = headerRect.yMax;
            dialog.DoColumnLabels(columnsRect);
            Widgets.DrawMenuSection(contentRect);
            contentRect = contentRect.ContractedBy(1f);

            List<DrugPolicyEntry> visibleEntries = GetVisibleEntries(selectedPolicy, state);
            if (visibleEntries.Count == 0)
            {
                GUI.color = Color.grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                TaggedString emptyLabel = state.SearchWidget.filter.Active
                    ? "DrugPolicySearch.NoMatches".Translate(state.SearchWidget.filter.Text)
                    : "NoDrugs".Translate();
                Widgets.Label(contentRect, emptyLabel);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float height = visibleEntries.Count * DrugEntryRowHeight;
            Rect viewRect = new Rect(0f, 0f, contentRect.width, height);
            Widgets.AdjustRectsForScrollView(rect, ref contentRect, ref viewRect);
            Vector2 scrollPosition = dialog.scrollPosition;
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);
            for (int i = 0; i < visibleEntries.Count; i++)
            {
                Rect rowRect = new Rect(0f, i * DrugEntryRowHeight, viewRect.width, DrugEntryRowHeight);
                dialog.DoEntryRow(rowRect, visibleEntries[i], i);
            }
            Widgets.EndScrollView();
            dialog.scrollPosition = scrollPosition;
        }

        private static DrugPolicy GetSelectedPolicy(Dialog_ManageDrugPolicies dialog)
        {
            return dialog.policyInt;
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

            for (int i = 0; i < selectedPolicy.Count; i++)
            {
                DrugPolicyEntry entry = selectedPolicy[i];
                if (state.SearchWidget.filter.Matches(entry.drug))
                {
                    state.VisibleEntries.Add(entry);
                }
            }

            return state.VisibleEntries;
        }

        private static void OnSearchChanged(Dialog_ManageDrugPolicies dialog, DrugPolicy selectedPolicy, SearchState state)
        {
            dialog.scrollPosition = Vector2.zero;
            state.CacheDirty = true;
            UpdateSearchFeedback(selectedPolicy, state);
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

    [HarmonyPatch(typeof(Dialog_ManageDrugPolicies), "get_OffsetHeaderY")]
    public static class Dialog_ManageDrugPolicies_OffsetHeaderY_SearchPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> patchedInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldc_R4, 64f),
                new CodeInstruction(OpCodes.Ret)
            };

            Logger.Message("Transpiler succeeded: Dialog_ManageDrugPolicies.get_OffsetHeaderY");
            return patchedInstructions;
        }
    }
}
