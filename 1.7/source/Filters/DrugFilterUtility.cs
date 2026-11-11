using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Drug_Policy_Search.Filters
{
    internal static class DrugFilterUtility
    {
        private const FoodTypeFlags EdibleFoodTypes = FoodTypeFlags.VegetableOrFruit |
            FoodTypeFlags.Meat | FoodTypeFlags.Meal | FoodTypeFlags.AnimalProduct |
            FoodTypeFlags.Fungus | FoodTypeFlags.Seed;

        private static readonly Dictionary<ThingDef, DrugUseMethod> UseMethods =
            new Dictionary<ThingDef, DrugUseMethod>();

        internal static bool Matches(DrugPolicyEntry entry, DrugUseMethod? useMethod,
            TechLevel? techLevel, DrugCategory? category)
        {
            ThingDef drug = entry?.drug;
            return drug != null &&
                (!techLevel.HasValue || drug.techLevel == techLevel.Value) &&
                (!category.HasValue || drug.ingestible?.drugCategory == category.Value) &&
                (!useMethod.HasValue || GetUseMethod(drug) == useMethod.Value);
        }

        internal static DrugUseMethod GetUseMethod(ThingDef drug)
        {
            if (drug == null)
            {
                return DrugUseMethod.Other;
            }

            if (!UseMethods.TryGetValue(drug, out DrugUseMethod method))
            {
                method = Classify(drug);
                UseMethods.Add(drug, method);
            }

            return method;
        }

        private static DrugUseMethod Classify(ThingDef drug)
        {
            IngestibleProperties ingestible = drug.ingestible;
            if (ingestible == null)
            {
                return DrugUseMethod.Other;
            }

            string sound = ingestible.ingestSound?.defName;
            switch (sound)
            {
                case "Ingest_Beer":
                case "Ingest_Drink":
                    return DrugUseMethod.Drink;
                case "Ingest_Smoke":
                    return DrugUseMethod.Smoke;
                case "Ingest_Inject":
                    return DrugUseMethod.Inject;
                case "Ingest_Snort":
                    return DrugUseMethod.Snort;
                case "Ingest_Pill":
                    return DrugUseMethod.Pill;
            }

            // A custom sound takes precedence over food type, even for edible items.
            if (sound != null)
            {
                return DrugUseMethod.Other;
            }

            return (ingestible.foodType & EdibleFoodTypes) != 0
                ? DrugUseMethod.Eat
                : DrugUseMethod.Other;
        }
    }
}
