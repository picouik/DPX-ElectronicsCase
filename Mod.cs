using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;

namespace DPX.ElectronicsCase;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "DPX-ElectronicsCase";
    public override string Name { get; init; } = "DPX Electronics Case";
    public override string Author { get; init; } = "TKV";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = true;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public sealed class Mod : IOnLoad
{
    private readonly ISptLogger<Mod> logger;
    private readonly DatabaseService databaseService;
    private readonly CustomItemService customItemService;

    public Mod(ISptLogger<Mod> logger, DatabaseService databaseService, CustomItemService customItemService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.customItemService = customItemService;
    }

    public Task OnLoad()
    {
        DpxElectronicsCaseInstaller.Apply(this.logger, this.databaseService, this.customItemService);
        return Task.CompletedTask;
    }
}

internal static class DpxElectronicsCaseInstaller
{
    private const string ModName = "DPX Electronics Case";
    private const string ItemCaseTpl = "59fb042886f7746c5005a7b2";
    private const string CommonContainerParent = "5795f317245977243854e041";
    private const string DpxCaseTpl = "d9e200000000000000000001";
    private const string DpxGridId = "d9e200000000000000000002";
    private const string TraderAssortId = "d9e200000000000000000003";
    private const string SkierId = "58330581ace78e27b8b10cee";
    private const string RoublesTpl = "5449016a4bdc2d6f028b456f";
    private const string BundlePrefabPath = "dpx/electronics_case.bundle";
    private const int Price = 250000;

    private static readonly string[] Whitelist =
    [
        "5734779624597737e04bf329",
        "573477e124597737dd42e191",
        "57347baf24597738002c6178",
        "590a386e86f77429692b27ab",
        "5a29276886f77435ed1b117c",
        "590a3b0486f7743954552bdb",
        "590c392f86f77444754deb29",
        "5af0561e86f7745f5f3ad6ac",
        "5c052f6886f7746b1e3db148",
        "5c052fb986f7746b2101e909",
        "5c05300686f7746dce784e5d",
        "5c05308086f7746b2101e90b",
        "5d0375ff86f774186372f685",
        "5d0376a486f7747d8050965c",
        "6389c7750ef44505c87f5996"
    ];

    public static void Apply(ISptLogger<Mod> logger, DatabaseService databaseService, CustomItemService customItemService)
    {
        ValidateBaseData(databaseService);
        CreateItem(databaseService, customItemService);
        ConfigureContainerGrid(databaseService);
        AddSkierOffer(databaseService);
        VerifyStartup(logger, databaseService);
    }

    private static void ValidateBaseData(DatabaseService databaseService)
    {
        var items = databaseService.GetItems();
        var missing = Whitelist
            .Concat([ItemCaseTpl, RoublesTpl])
            .Where(id => !items.ContainsKey(new MongoId(id)))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"[{ModName}] missing template id(s): {string.Join(", ", missing)}");
        }

        if (databaseService.GetTrader(new MongoId(SkierId))?.Assort == null)
        {
            throw new InvalidOperationException($"[{ModName}] Skier trader/assort not found: {SkierId}");
        }
    }

    private static void CreateItem(DatabaseService databaseService, CustomItemService customItemService)
    {
        var items = databaseService.GetItems();
        items.Remove(new MongoId(DpxCaseTpl));

        var baseHandbookEntry = databaseService.GetHandbook().Items.FirstOrDefault(item => item.Id.Equals(ItemCaseTpl))
            ?? throw new InvalidOperationException($"[{ModName}] base handbook entry not found: {ItemCaseTpl}");

        var props = new TemplateItemProperties();
        SetValue(props, "Name", "DPX Electronics Case");
        SetValue(props, "ShortName", "DPX Elec");
        SetValue(props, "Description", "A rugged storage case designed specifically for electronic components, military electronics, storage devices, processors, circuit boards and advanced technology parts.");
        SetValue(props, "Width", 3);
        SetValue(props, "Height", 3);
        SetValue(props, "BackgroundColor", "black");
        SetValue(props, "ExaminedByDefault", true);

        var result = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
        {
            ItemTplToClone = new MongoId(ItemCaseTpl),
            OverrideProperties = props,
            ParentId = CommonContainerParent,
            NewId = DpxCaseTpl,
            FleaPriceRoubles = Price,
            HandbookPriceRoubles = Price,
            HandbookParentId = baseHandbookEntry.ParentId,
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new()
                {
                    Name = "DPX Electronics Case",
                    ShortName = "DPX Elec",
                    Description = "A rugged storage case designed specifically for electronic components, military electronics, storage devices, processors, circuit boards and advanced technology parts."
                }
            }
        });

        if (result.Success != true)
        {
            throw new InvalidOperationException($"[{ModName}] item creation failed: {string.Join("; ", result.Errors ?? [])}");
        }

        ConfigurePrefab(databaseService);
    }

    private static void ConfigurePrefab(DatabaseService databaseService)
    {
        var item = databaseService.GetItems()[new MongoId(DpxCaseTpl)];
        var props = GetMemberValue(item, "Properties", "Props", "_props")
            ?? throw new InvalidOperationException($"[{ModName}] created item props not found");

        var prefab = GetMemberValue(props, "Prefab", "prefab")
            ?? throw new InvalidOperationException($"[{ModName}] created item prefab not found");

        SetValue(prefab, "Path", BundlePrefabPath);
    }

    private static void ConfigureContainerGrid(DatabaseService databaseService)
    {
        var item = databaseService.GetItems()[new MongoId(DpxCaseTpl)];
        var props = GetMemberValue(item, "Properties", "Props", "_props")
            ?? throw new InvalidOperationException($"[{ModName}] created item props not found");

        var grids = GetMemberValue(props, "Grids", "grids") as IEnumerable
            ?? throw new InvalidOperationException($"[{ModName}] created item grids not found");

        var grid = grids.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException($"[{ModName}] created item has no grid");

        SetValue(grid, "Id", new MongoId(DpxGridId));
        SetValue(grid, "Parent", new MongoId(DpxCaseTpl));
        SetValue(grid, "Name", "main");

        var gridProps = GetMemberValue(grid, "Properties", "Props", "_props")
            ?? throw new InvalidOperationException($"[{ModName}] grid props not found");

        SetValue(gridProps, "CellsH", 10);
        SetValue(gridProps, "CellsV", 10);
        SetValue(gridProps, "MinCount", 0d);
        SetValue(gridProps, "MaxCount", 0d);
        SetValue(gridProps, "MaxWeight", 0d);
        SetValue(gridProps, "IsSortingTable", false);

        var filters = GetMemberValue(gridProps, "Filters", "filters") as IEnumerable
            ?? throw new InvalidOperationException($"[{ModName}] grid filters not found");
        var filter = filters.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException($"[{ModName}] grid has no filter entry");

        SetValue(filter, "Filter", Whitelist.Select(id => new MongoId(id)).ToHashSet());
        SetValue(filter, "ExcludedFilter", new HashSet<MongoId>());
    }

    private static void AddSkierOffer(DatabaseService databaseService)
    {
        var skier = databaseService.GetTrader(new MongoId(SkierId));
        var assort = skier?.Assort
            ?? throw new InvalidOperationException($"[{ModName}] Skier assort not found");

        var assortId = new MongoId(TraderAssortId);
        assort.Items.RemoveAll(item => item.Id.Equals(assortId));
        assort.BarterScheme.Remove(assortId);
        assort.LoyalLevelItems.Remove(assortId);

        assort.Items.Add(new Item
        {
            Id = assortId,
            Template = new MongoId(DpxCaseTpl),
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd
            {
                UnlimitedCount = true,
                StackObjectsCount = 999999,
                BuyRestrictionMax = 999999,
                BuyRestrictionCurrent = 0
            }
        });

        assort.BarterScheme[assortId] =
        [
            [
                new BarterScheme
                {
                    Template = new MongoId(RoublesTpl),
                    Count = Price
                }
            ]
        ];
        assort.LoyalLevelItems[assortId] = 1;
    }

    private static void VerifyStartup(ISptLogger<Mod> logger, DatabaseService databaseService)
    {
        var item = databaseService.GetItems()[new MongoId(DpxCaseTpl)];
        var props = GetMemberValue(item, "Properties", "Props", "_props")!;
        var grids = ((IEnumerable)GetMemberValue(props, "Grids", "grids")!).Cast<object>().ToList();
        var grid = grids.Single();
        var gridProps = GetMemberValue(grid, "Properties", "Props", "_props")!;
        var filters = ((IEnumerable)GetMemberValue(gridProps, "Filters", "filters")!).Cast<object>().ToList();
        var filter = filters.Single();
        var allowed = ((IEnumerable)GetMemberValue(filter, "Filter")!).Cast<object>().Select(value => value.ToString()).ToList();

        var sizeOk = Convert.ToInt32(GetMemberValue(props, "Width", "width")) == 3
            && Convert.ToInt32(GetMemberValue(props, "Height", "height")) == 3;
        var gridOk = Convert.ToInt32(GetMemberValue(gridProps, "CellsH", "cellsH")) == 10
            && Convert.ToInt32(GetMemberValue(gridProps, "CellsV", "cellsV")) == 10;
        var filterOk = allowed.Count == Whitelist.Length && !allowed.Except(Whitelist, StringComparer.OrdinalIgnoreCase).Any();
        var skier = databaseService.GetTrader(new MongoId(SkierId));
        var assortId = new MongoId(TraderAssortId);
        var traderOk = skier?.Assort?.Items.Any(item => item.Id.Equals(assortId) && item.Template.Equals(new MongoId(DpxCaseTpl))) == true
            && skier.Assort.BarterScheme.ContainsKey(assortId)
            && skier.Assort.LoyalLevelItems.TryGetValue(assortId, out var loyaltyLevel)
            && loyaltyLevel == 1;
        var prefabPath = GetMemberValue(GetMemberValue(props, "Prefab", "prefab")!, "Path", "path")?.ToString();
        var prefabOk = string.Equals(prefabPath, BundlePrefabPath, StringComparison.OrdinalIgnoreCase);

        if (!sizeOk || !gridOk || !filterOk || !traderOk || !prefabOk)
        {
            throw new InvalidOperationException($"[{ModName}] startup verification failed: sizeOk={sizeOk} gridOk={gridOk} filterOk={filterOk} traderOk={traderOk} prefabOk={prefabOk} prefabPath={prefabPath}");
        }

        logger.Info($"[{ModName}] loaded", null);
    }

    private static object? GetMemberValue(object target, params string[] names)
    {
        var type = target.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null)
            {
                return property.GetValue(target);
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                return field.GetValue(target);
            }
        }

        return null;
    }

    private static void SetValue(object target, string name, object? value)
    {
        var type = target.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, ConvertValue(value, property.PropertyType));
            return;
        }

        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null)
        {
            field.SetValue(target, ConvertValue(value, field.FieldType));
            return;
        }

        throw new InvalidOperationException($"[{ModName}] unable to set {name} on {type.FullName}");
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            return null;
        }

        if (targetType == typeof(MongoId) && value is string stringValue)
        {
            return new MongoId(stringValue);
        }

        if (targetType == typeof(MongoId?) && value is string nullableStringValue)
        {
            return (MongoId?)new MongoId(nullableStringValue);
        }

        if (targetType == typeof(string))
        {
            return value.ToString();
        }

        if (value is IList<MongoId> mongoIds && targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)Activator.CreateInstance(targetType)!;
            foreach (var id in mongoIds)
            {
                list.Add(targetType.GetGenericArguments()[0] == typeof(string) ? id.ToString() : id);
            }

            return list;
        }

        if (value is IList<MongoId> mongoIdArrayValues && targetType.IsArray)
        {
            var elementType = targetType.GetElementType()!;
            var array = Array.CreateInstance(elementType, mongoIdArrayValues.Count);
            for (var index = 0; index < mongoIdArrayValues.Count; index++)
            {
                array.SetValue(elementType == typeof(string) ? mongoIdArrayValues[index].ToString() : mongoIdArrayValues[index], index);
            }

            return array;
        }

        if (targetType.IsAssignableFrom(value.GetType()))
        {
            return value;
        }

        return value;
    }
}
