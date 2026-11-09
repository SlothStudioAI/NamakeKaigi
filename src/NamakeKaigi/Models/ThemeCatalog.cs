using System.Windows;
using System.Windows.Media;

namespace NamakeKaigi.Models;

/// <summary>画面の配色テーマ1つ分の選択肢。</summary>
/// <param name="Id">settings.json に保存する識別名</param>
/// <param name="Label">画面の選択欄に出す名前</param>
/// <param name="IsDark">ダークモードならtrue</param>
/// <param name="Colors">リソース名と色コード(#RRGGBB)の対応表</param>
public sealed record ThemePreset(
    string Id,
    string Label,
    bool IsDark,
    IReadOnlyDictionary<string, string> Colors);

/// <summary>緑基調のバリエーションとダークモードの定義一覧、および画面リソースへの即時適用。</summary>
public static class ThemeCatalog
{
    public const string DefaultThemeId = "forest";

    /// <summary>選べるすべての配色テーマ。</summary>
    public static IReadOnlyList<ThemePreset> All { get; } = new[]
    {
        new ThemePreset(
            "forest",
            "🌲 森の緑（標準）",
            IsDark: false,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#2F6B45",
                ["SidebarBorderBrush"] = "#4C8A63",
                ["SidebarHoverBrush"] = "#35704A",
                ["SidebarSelectedBrush"] = "#3B7D52",
                ["SidebarInputBackgroundBrush"] = "#255436",
                ["SidebarTextBrush"] = "#F2F8EE",
                ["SidebarMutedTextBrush"] = "#CFE3CF",
                ["SidebarAccentBrush"] = "#D9F28A",
                ["SidebarLogoBrush"] = "#6DB67F",
                ["MainBackgroundBrush"] = "#FBF6DC",
                ["PanelBackgroundBrush"] = "#FFFCEF",
                ["PanelBorderBrush"] = "#E2E8F0",
                ["TitleBarBorderBrush"] = "#4C8A63",
                ["TitleBarTextBrush"] = "#F7FBF1",
                ["TitleBarButtonForegroundBrush"] = "#F2F8EE",
                ["TitleBarButtonHoverBrush"] = "#3B7D52",
                ["TitleBarButtonPressedBrush"] = "#1F4D32",
                ["TextPrimaryBrush"] = "#3B2A1E",
                ["TextMutedBrush"] = "#6B7762",
                ["AccentGreenBrush"] = "#2F6B45",
                ["LinkTextBrush"] = "#2F6B45",
                ["AccentGreenHoverBrush"] = "#265A39",
                ["AccentGreenPressedBrush"] = "#1F4D32",
                ["InputBackgroundBrush"] = "#FFFFFF",
                ["InputBorderBrush"] = "#D5DCC8",
                ["InputFocusBorderBrush"] = "#2F6B45",
                ["SecondaryButtonBackgroundBrush"] = "#FFFCEF",
                ["PanelInsetBrush"] = "#F4F1DA",
                ["PopupBorderBrush"] = "#D5DCC8",
                ["MenuItemHighlightBrush"] = "#E6EFD8",
            }),

        new ThemePreset(
            "mint",
            "🌿 若葉ミント（爽やか）",
            IsDark: false,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#1F6E50",
                ["SidebarBorderBrush"] = "#3D8F6F",
                ["SidebarHoverBrush"] = "#257A59",
                ["SidebarSelectedBrush"] = "#2D8A66",
                ["SidebarInputBackgroundBrush"] = "#17563E",
                ["SidebarTextBrush"] = "#F2FBF7",
                ["SidebarMutedTextBrush"] = "#C6E6D8",
                ["SidebarAccentBrush"] = "#B8F7D4",
                ["SidebarLogoBrush"] = "#56B890",
                ["MainBackgroundBrush"] = "#F0F7F4",
                ["PanelBackgroundBrush"] = "#FFFFFF",
                ["PanelBorderBrush"] = "#D5E6DF",
                ["TitleBarBorderBrush"] = "#3D8F6F",
                ["TitleBarTextBrush"] = "#F2FBF7",
                ["TitleBarButtonForegroundBrush"] = "#F2FBF7",
                ["TitleBarButtonHoverBrush"] = "#2D8A66",
                ["TitleBarButtonPressedBrush"] = "#15523B",
                ["TextPrimaryBrush"] = "#183028",
                ["TextMutedBrush"] = "#567368",
                ["AccentGreenBrush"] = "#1F6E50",
                ["LinkTextBrush"] = "#1F6E50",
                ["AccentGreenHoverBrush"] = "#185940",
                ["AccentGreenPressedBrush"] = "#124531",
                ["InputBackgroundBrush"] = "#FFFFFF",
                ["InputBorderBrush"] = "#C2DBD0",
                ["InputFocusBorderBrush"] = "#1F6E50",
                ["SecondaryButtonBackgroundBrush"] = "#F7FBF9",
                ["PanelInsetBrush"] = "#E3F0EA",
                ["PopupBorderBrush"] = "#C2DBD0",
                ["MenuItemHighlightBrush"] = "#D8EFE5",
            }),

        new ThemePreset(
            "matcha",
            "🍵 抹茶・深緑（落ち着き）",
            IsDark: false,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#3B5236",
                ["SidebarBorderBrush"] = "#577350",
                ["SidebarHoverBrush"] = "#445E3E",
                ["SidebarSelectedBrush"] = "#4E6B47",
                ["SidebarInputBackgroundBrush"] = "#2E402A",
                ["SidebarTextBrush"] = "#F3F6F0",
                ["SidebarMutedTextBrush"] = "#CBD6C5",
                ["SidebarAccentBrush"] = "#DFEEA6",
                ["SidebarLogoBrush"] = "#789A6E",
                ["MainBackgroundBrush"] = "#ECEFE6",
                ["PanelBackgroundBrush"] = "#F8FAF5",
                ["PanelBorderBrush"] = "#D6DEC9",
                ["TitleBarBorderBrush"] = "#577350",
                ["TitleBarTextBrush"] = "#F3F6F0",
                ["TitleBarButtonForegroundBrush"] = "#F3F6F0",
                ["TitleBarButtonHoverBrush"] = "#4E6B47",
                ["TitleBarButtonPressedBrush"] = "#2B3D27",
                ["TextPrimaryBrush"] = "#283024",
                ["TextMutedBrush"] = "#5E6B58",
                ["AccentGreenBrush"] = "#3B5236",
                ["LinkTextBrush"] = "#3B5236",
                ["AccentGreenHoverBrush"] = "#30442B",
                ["AccentGreenPressedBrush"] = "#253621",
                ["InputBackgroundBrush"] = "#FFFFFF",
                ["InputBorderBrush"] = "#C7D1BA",
                ["InputFocusBorderBrush"] = "#3B5236",
                ["SecondaryButtonBackgroundBrush"] = "#F8FAF5",
                ["PanelInsetBrush"] = "#E2E7D8",
                ["PopupBorderBrush"] = "#C7D1BA",
                ["MenuItemHighlightBrush"] = "#DCE4CF",
            }),

        new ThemePreset(
            "dark-forest",
            "🌙 夜の森（ダーク）",
            IsDark: true,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#14261C",
                ["SidebarBorderBrush"] = "#2A4D39",
                ["SidebarHoverBrush"] = "#1C3426",
                ["SidebarSelectedBrush"] = "#264734",
                ["SidebarInputBackgroundBrush"] = "#0F1D15",
                ["SidebarTextBrush"] = "#E8F2EC",
                ["SidebarMutedTextBrush"] = "#9BB8A6",
                ["SidebarAccentBrush"] = "#C8E87B",
                ["SidebarLogoBrush"] = "#3D8558",
                ["MainBackgroundBrush"] = "#18221D",
                ["PanelBackgroundBrush"] = "#212F28",
                ["PanelBorderBrush"] = "#2F453A",
                ["TitleBarBorderBrush"] = "#2A4D39",
                ["TitleBarTextBrush"] = "#E8F2EC",
                ["TitleBarButtonForegroundBrush"] = "#E8F2EC",
                ["TitleBarButtonHoverBrush"] = "#264734",
                ["TitleBarButtonPressedBrush"] = "#102017",
                ["TextPrimaryBrush"] = "#E6F0EA",
                ["TextMutedBrush"] = "#96B0A0",
                ["AccentGreenBrush"] = "#387D53",
                ["LinkTextBrush"] = "#7CE39F",
                ["AccentGreenHoverBrush"] = "#439162",
                ["AccentGreenPressedBrush"] = "#2D6643",
                ["InputBackgroundBrush"] = "#141D18",
                ["InputBorderBrush"] = "#355042",
                ["InputFocusBorderBrush"] = "#4FA370",
                ["SecondaryButtonBackgroundBrush"] = "#26362E",
                ["PanelInsetBrush"] = "#293B32",
                ["PopupBorderBrush"] = "#355042",
                ["MenuItemHighlightBrush"] = "#264233",
            }),

        new ThemePreset(
            "dark-slate",
            "🌌 ダーク・エメラルド",
            IsDark: true,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#151A18",
                ["SidebarBorderBrush"] = "#2B3833",
                ["SidebarHoverBrush"] = "#1E2623",
                ["SidebarSelectedBrush"] = "#27332F",
                ["SidebarInputBackgroundBrush"] = "#101412",
                ["SidebarTextBrush"] = "#ECF2EF",
                ["SidebarMutedTextBrush"] = "#95A8A0",
                ["SidebarAccentBrush"] = "#7EE787",
                ["SidebarLogoBrush"] = "#2EA06B",
                ["MainBackgroundBrush"] = "#1B201E",
                ["PanelBackgroundBrush"] = "#242B28",
                ["PanelBorderBrush"] = "#323D39",
                ["TitleBarBorderBrush"] = "#2B3833",
                ["TitleBarTextBrush"] = "#ECF2EF",
                ["TitleBarButtonForegroundBrush"] = "#ECF2EF",
                ["TitleBarButtonHoverBrush"] = "#27332F",
                ["TitleBarButtonPressedBrush"] = "#101412",
                ["TextPrimaryBrush"] = "#ECF2EF",
                ["TextMutedBrush"] = "#95A8A0",
                ["AccentGreenBrush"] = "#288A5B",
                ["LinkTextBrush"] = "#6EE7B7",
                ["AccentGreenHoverBrush"] = "#31A16B",
                ["AccentGreenPressedBrush"] = "#1F6E48",
                ["InputBackgroundBrush"] = "#141816",
                ["InputBorderBrush"] = "#35423D",
                ["InputFocusBorderBrush"] = "#38B57C",
                ["SecondaryButtonBackgroundBrush"] = "#2A332F",
                ["PanelInsetBrush"] = "#2D3733",
                ["PopupBorderBrush"] = "#35423D",
                ["MenuItemHighlightBrush"] = "#233B30",
            }),

        new ThemePreset(
            "office-light",
            "💼 オフィス・ライト",
            IsDark: false,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#1E293B",
                ["SidebarBorderBrush"] = "#334155",
                ["SidebarHoverBrush"] = "#273549",
                ["SidebarSelectedBrush"] = "#334155",
                ["SidebarInputBackgroundBrush"] = "#0F172A",
                ["SidebarTextBrush"] = "#F8FAFC",
                ["SidebarMutedTextBrush"] = "#94A3B8",
                ["SidebarAccentBrush"] = "#93C5FD",
                ["SidebarLogoBrush"] = "#3B82F6",
                ["MainBackgroundBrush"] = "#F1F5F9",
                ["PanelBackgroundBrush"] = "#FFFFFF",
                ["PanelBorderBrush"] = "#CBD5E1",
                ["TitleBarBorderBrush"] = "#334155",
                ["TitleBarTextBrush"] = "#F8FAFC",
                ["TitleBarButtonForegroundBrush"] = "#F8FAFC",
                ["TitleBarButtonHoverBrush"] = "#334155",
                ["TitleBarButtonPressedBrush"] = "#0F172A",
                ["TextPrimaryBrush"] = "#0F172A",
                ["TextMutedBrush"] = "#475569",
                ["AccentGreenBrush"] = "#2563EB",
                ["LinkTextBrush"] = "#2563EB",
                ["AccentGreenHoverBrush"] = "#1D4ED8",
                ["AccentGreenPressedBrush"] = "#1E40AF",
                ["InputBackgroundBrush"] = "#FFFFFF",
                ["InputBorderBrush"] = "#CBD5E1",
                ["InputFocusBorderBrush"] = "#2563EB",
                ["SecondaryButtonBackgroundBrush"] = "#F8FAFC",
                ["PanelInsetBrush"] = "#E2E8F0",
                ["PopupBorderBrush"] = "#CBD5E1",
                ["MenuItemHighlightBrush"] = "#DBEAFE",
            }),

        new ThemePreset(
            "office-dark",
            "💼 オフィス・ダーク",
            IsDark: true,
            new Dictionary<string, string>
            {
                ["SidebarBackgroundBrush"] = "#0F172A",
                ["SidebarBorderBrush"] = "#1E293B",
                ["SidebarHoverBrush"] = "#1E293B",
                ["SidebarSelectedBrush"] = "#334155",
                ["SidebarInputBackgroundBrush"] = "#090E1A",
                ["SidebarTextBrush"] = "#F1F5F9",
                ["SidebarMutedTextBrush"] = "#94A3B8",
                ["SidebarAccentBrush"] = "#93C5FD",
                ["SidebarLogoBrush"] = "#3B82F6",
                ["MainBackgroundBrush"] = "#131C2E",
                ["PanelBackgroundBrush"] = "#1E293B",
                ["PanelBorderBrush"] = "#334155",
                ["TitleBarBorderBrush"] = "#1E293B",
                ["TitleBarTextBrush"] = "#F1F5F9",
                ["TitleBarButtonForegroundBrush"] = "#F1F5F9",
                ["TitleBarButtonHoverBrush"] = "#334155",
                ["TitleBarButtonPressedBrush"] = "#090E1A",
                ["TextPrimaryBrush"] = "#F1F5F9",
                ["TextMutedBrush"] = "#94A3B8",
                ["AccentGreenBrush"] = "#2563EB",
                ["LinkTextBrush"] = "#60A5FA",
                ["AccentGreenHoverBrush"] = "#3B82F6",
                ["AccentGreenPressedBrush"] = "#1D4ED8",
                ["InputBackgroundBrush"] = "#0F172A",
                ["InputBorderBrush"] = "#334155",
                ["InputFocusBorderBrush"] = "#3B82F6",
                ["SecondaryButtonBackgroundBrush"] = "#273549",
                ["PanelInsetBrush"] = "#273549",
                ["PopupBorderBrush"] = "#334155",
                ["MenuItemHighlightBrush"] = "#1E3A5F",
            }),
    };

    /// <summary>テーマIDから定義を探す(見つからなければ標準の forest を返す)。</summary>
    public static ThemePreset GetOrDefault(string? themeId) =>
        All.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase))
        ?? All[0];

    /// <summary>指定したテーマの色を、現在の Application.Resources に即時反映する。</summary>
    /// <param name="themeId">適用するテーマID</param>
    /// <param name="resources">対象のリソース辞書(省略時は Application.Current.Resources)</param>
    public static void Apply(string? themeId, ResourceDictionary? resources = null)
    {
        if (resources is null && Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Apply(themeId, null));
            return;
        }

        var target = resources ?? Application.Current?.Resources;
        if (target is null) return;

        var preset = GetOrDefault(themeId);
        foreach (var (key, hex) in preset.Colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (target[key] is SolidColorBrush existing && !existing.IsFrozen)
            {
                existing.Color = color;
            }
            else
            {
                target[key] = new SolidColorBrush(color);
            }
        }
        if (resources is null) Applied?.Invoke();
    }

    /// <summary>画面全体にテーマを当てたあとに知らせる(その場で色を取って描いた部分を、描き直すため)。</summary>
    public static event Action? Applied;
}
