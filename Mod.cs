using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.0");
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
    private const string ElectronicsCategoryTpl = "57864a66245977548f04a81f";
    private const int DefaultPrice = 250000;
    private const int DefaultCaseWidth = 3;
    private const int DefaultCaseHeight = 3;
    private const int DefaultGridWidth = 10;
    private const int DefaultGridHeight = 10;
    private const int DefaultLoyaltyLevel = 1;
    private const string DefaultTrader = "Skier";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Dictionary<string, string> TraderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Prapor"] = "54cb50c76803fa8b248b4571",
        ["Therapist"] = "54cb57776803fa99248b456e",
        ["Fence"] = "579dc571d53a0658a154fbec",
        ["Skier"] = SkierId,
        ["Peacekeeper"] = "5935c25fb3acc3127c3d8cd9",
        ["Mechanic"] = "5a7c2eca46aef81a7ca2145d",
        ["Ragman"] = "5ac3b934156ae10c4430e83c",
        ["Jaeger"] = "5c0647fdd443bc2504c2d371"
    };

    public static void Apply(ISptLogger<Mod> logger, DatabaseService databaseService, CustomItemService customItemService)
    {
        var config = LoadConfig();
        ValidateBaseData(databaseService, config);
        CreateItem(databaseService, customItemService, config);
        ConfigureContainerGrid(databaseService, config);
        AddTraderOffer(databaseService, config);
        VerifyStartup(logger, databaseService, config);
    }

    private static ModConfig LoadConfig()
    {
        var configPath = GetConfigPath();
        var defaultConfig = ModConfig.Default();

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(configPath)!);
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, JsonOptions));
            return defaultConfig.ToValidated();
        }

        try
        {
            var rawConfig = JsonSerializer.Deserialize<ModConfig>(File.ReadAllText(configPath), JsonOptions);
            return (rawConfig ?? defaultConfig).ToValidated();
        }
        catch
        {
            return defaultConfig.ToValidated();
        }
    }

    private static string GetConfigPath()
    {
        var modFolder = System.IO.Path.GetDirectoryName(typeof(Mod).Assembly.Location)
            ?? AppContext.BaseDirectory;
        return System.IO.Path.Combine(modFolder, "config", "config.json");
    }

    private static void ValidateBaseData(DatabaseService databaseService, ModConfig config)
    {
        var items = databaseService.GetItems();
        var missing = new[] { ItemCaseTpl, RoublesTpl }
            .Where(id => !items.ContainsKey(new MongoId(id)))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"[{ModName}] missing template id(s): {string.Join(", ", missing)}");
        }

        if (databaseService.GetTrader(new MongoId(config.TraderId))?.Assort == null)
        {
            throw new InvalidOperationException($"[{ModName}] trader/assort not found: {config.TraderId}");
        }
    }

    private static void CreateItem(DatabaseService databaseService, CustomItemService customItemService, ModConfig config)
    {
        var items = databaseService.GetItems();
        items.Remove(new MongoId(DpxCaseTpl));

        var baseHandbookEntry = databaseService.GetHandbook().Items.FirstOrDefault(item => item.Id.Equals(ItemCaseTpl))
            ?? throw new InvalidOperationException($"[{ModName}] base handbook entry not found: {ItemCaseTpl}");

        var props = new TemplateItemProperties();
        SetValue(props, "Name", "DPX Electronics Case");
        SetValue(props, "ShortName", "DPX Elec");
        SetValue(props, "Description", "A rugged storage case designed specifically for electronic components, military electronics, storage devices, processors, circuit boards and advanced technology parts.");
        SetValue(props, "Width", config.CaseSize.Width);
        SetValue(props, "Height", config.CaseSize.Height);
        SetValue(props, "BackgroundColor", "black");
        SetValue(props, "ExaminedByDefault", true);

        var result = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
        {
            ItemTplToClone = new MongoId(ItemCaseTpl),
            OverrideProperties = props,
            ParentId = CommonContainerParent,
            NewId = DpxCaseTpl,
            FleaPriceRoubles = config.Price,
            HandbookPriceRoubles = config.Price,
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

    private static void ConfigureContainerGrid(DatabaseService databaseService, ModConfig config)
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

        SetValue(gridProps, "CellsH", config.InternalGrid.Width);
        SetValue(gridProps, "CellsV", config.InternalGrid.Height);
        SetValue(gridProps, "MinCount", 0d);
        SetValue(gridProps, "MaxCount", 0d);
        SetValue(gridProps, "MaxWeight", 0d);
        SetValue(gridProps, "IsSortingTable", false);

        var filters = GetMemberValue(gridProps, "Filters", "filters") as IEnumerable
            ?? throw new InvalidOperationException($"[{ModName}] grid filters not found");
        var filter = filters.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException($"[{ModName}] grid has no filter entry");

        SetValue(filter, "Filter", config.FilterIds.Select(id => new MongoId(id)).ToHashSet());
        SetValue(filter, "ExcludedFilter", new HashSet<MongoId>());
    }

    private static void AddTraderOffer(DatabaseService databaseService, ModConfig config)
    {
        foreach (var traderId in TraderIds.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trader = databaseService.GetTrader(new MongoId(traderId));
            if (trader?.Assort == null)
            {
                continue;
            }

            var oldAssortId = new MongoId(TraderAssortId);
            trader.Assort.Items.RemoveAll(item => item.Id.Equals(oldAssortId));
            trader.Assort.BarterScheme.Remove(oldAssortId);
            trader.Assort.LoyalLevelItems.Remove(oldAssortId);
        }

        var targetTrader = databaseService.GetTrader(new MongoId(config.TraderId));
        var assort = targetTrader?.Assort
            ?? throw new InvalidOperationException($"[{ModName}] trader assort not found: {config.TraderId}");

        var assortId = new MongoId(TraderAssortId);
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
                    Count = config.Price
                }
            ]
        ];
        assort.LoyalLevelItems[assortId] = config.LoyaltyLevel;
    }

    private static void VerifyStartup(ISptLogger<Mod> logger, DatabaseService databaseService, ModConfig config)
    {
        var item = databaseService.GetItems()[new MongoId(DpxCaseTpl)];
        var props = GetMemberValue(item, "Properties", "Props", "_props")!;
        var grids = ((IEnumerable)GetMemberValue(props, "Grids", "grids")!).Cast<object>().ToList();
        var grid = grids.Single();
        var gridProps = GetMemberValue(grid, "Properties", "Props", "_props")!;
        var filters = ((IEnumerable)GetMemberValue(gridProps, "Filters", "filters")!).Cast<object>().ToList();
        var filter = filters.Single();
        var allowed = ((IEnumerable)GetMemberValue(filter, "Filter")!).Cast<object>().Select(value => value.ToString()).ToList();

        var sizeOk = Convert.ToInt32(GetMemberValue(props, "Width", "width")) == config.CaseSize.Width
            && Convert.ToInt32(GetMemberValue(props, "Height", "height")) == config.CaseSize.Height;
        var gridOk = Convert.ToInt32(GetMemberValue(gridProps, "CellsH", "cellsH")) == config.InternalGrid.Width
            && Convert.ToInt32(GetMemberValue(gridProps, "CellsV", "cellsV")) == config.InternalGrid.Height;
        var filterOk = allowed.Count == config.FilterIds.Count && !allowed.Except(config.FilterIds, StringComparer.OrdinalIgnoreCase).Any();
        var trader = databaseService.GetTrader(new MongoId(config.TraderId));
        var assortId = new MongoId(TraderAssortId);
        var traderOk = trader?.Assort?.Items.Any(item => item.Id.Equals(assortId) && item.Template.Equals(new MongoId(DpxCaseTpl))) == true
            && trader.Assort.BarterScheme.ContainsKey(assortId)
            && trader.Assort.LoyalLevelItems.TryGetValue(assortId, out var loyaltyLevel)
            && loyaltyLevel == config.LoyaltyLevel;
        var prefabPath = GetMemberValue(GetMemberValue(props, "Prefab", "prefab")!, "Path", "path")?.ToString();
        var prefabOk = string.Equals(prefabPath, BundlePrefabPath, StringComparison.OrdinalIgnoreCase);

        if (!sizeOk || !gridOk || !filterOk || !traderOk || !prefabOk)
        {
            throw new InvalidOperationException($"[{ModName}] startup verification failed: sizeOk={sizeOk} gridOk={gridOk} filterOk={filterOk} traderOk={traderOk} prefabOk={prefabOk} prefabPath={prefabPath}");
        }

        logger.Info($"[{ModName}] loaded", null);
    }

    private sealed class ModConfig
    {
        public SizeConfig CaseSize { get; set; } = new(DefaultCaseWidth, DefaultCaseHeight);
        public SizeConfig InternalGrid { get; set; } = new(DefaultGridWidth, DefaultGridHeight);
        public int Price { get; set; } = DefaultPrice;
        public string Trader { get; set; } = DefaultTrader;
        public int LoyaltyLevel { get; set; } = DefaultLoyaltyLevel;
        public List<string> AcceptedCategories { get; set; } = [ElectronicsCategoryTpl];
        public List<string> AcceptedItems { get; set; } = [];

        [JsonIgnore]
        public string TraderId { get; private set; } = SkierId;

        [JsonIgnore]
        public List<string> FilterIds { get; private set; } = [ElectronicsCategoryTpl];

        public static ModConfig Default()
        {
            return new ModConfig
            {
                CaseSize = new SizeConfig(DefaultCaseWidth, DefaultCaseHeight),
                InternalGrid = new SizeConfig(DefaultGridWidth, DefaultGridHeight),
                Price = DefaultPrice,
                Trader = DefaultTrader,
                LoyaltyLevel = DefaultLoyaltyLevel,
                AcceptedCategories = [ElectronicsCategoryTpl],
                AcceptedItems = []
            };
        }

        public ModConfig ToValidated()
        {
            var validatedTrader = TraderIds.ContainsKey(Trader ?? string.Empty) ? Trader! : DefaultTrader;
            var validated = new ModConfig
            {
                CaseSize = new SizeConfig(
                    Clamp(CaseSize?.Width, 1, 10, DefaultCaseWidth),
                    Clamp(CaseSize?.Height, 1, 10, DefaultCaseHeight)),
                InternalGrid = new SizeConfig(
                    Clamp(InternalGrid?.Width, 1, 20, DefaultGridWidth),
                    Clamp(InternalGrid?.Height, 1, 20, DefaultGridHeight)),
                Price = Math.Max(1, Price),
                Trader = validatedTrader,
                LoyaltyLevel = Clamp(LoyaltyLevel, 1, 4, DefaultLoyaltyLevel),
                AcceptedCategories = ValidMongoIds(AcceptedCategories).ToList(),
                AcceptedItems = ValidMongoIds(AcceptedItems).ToList()
            };

            validated.TraderId = TraderIds[validatedTrader];
            validated.FilterIds = validated.AcceptedCategories
                .Concat(validated.AcceptedItems)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validated.FilterIds.Count == 0)
            {
                validated.AcceptedCategories = [ElectronicsCategoryTpl];
                validated.FilterIds = [ElectronicsCategoryTpl];
            }

            return validated;
        }

        private static int Clamp(int? value, int min, int max, int fallback)
        {
            return value is null ? fallback : Math.Min(max, Math.Max(min, value.Value));
        }

        private static IEnumerable<string> ValidMongoIds(IEnumerable<string>? ids)
        {
            return (ids ?? [])
                .Where(IsValidMongoId)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsValidMongoId(string? value)
        {
            return value?.Length == 24 && value.All(Uri.IsHexDigit);
        }
    }

    private sealed class SizeConfig
    {
        public SizeConfig()
        {
        }

        public SizeConfig(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; set; }
        public int Height { get; set; }
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
