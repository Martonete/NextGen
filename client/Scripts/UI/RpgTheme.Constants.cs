using Godot;
using System.Collections.Generic;

namespace ArgentumNextgen.UI;

public static partial class RpgTheme
{
    public const string AssetsPath = "res://Data/UI/";

    // =========================================================================
    // SPACING & MARGIN CONSTANTS
    // =========================================================================

    public const int SpacingSm = 4;
    public const int SpacingMd = 8;
    public const int SpacingLg = 12;
    public const int SpacingXl = 16;

    // Form margins (with title bar — BaseForm)
    public const int FormMarginTop = 58;
    public const int FormMarginLeft = 30;
    public const int FormMarginRight = 30;
    public const int FormMarginBottom = 20;

    // Panel margins (no title bar)
    public const int PanelMarginTop = 20;
    public const int PanelMarginLeft = 30;
    public const int PanelMarginRight = 30;
    public const int PanelMarginBottom = 20;

    // =========================================================================
    // DATA TYPES
    // =========================================================================

    public record struct PanelStyleData(string? Stretched = null, string? Texture = null, Vector4? NpMargins = null);
    public record struct CheckboxStyleData(string Frame, string Fill);
    public record struct MiniButtonData(string Normal, string Hover);

    // =========================================================================
    // PANEL STYLES
    // =========================================================================

    public static readonly Dictionary<string, PanelStyleData> PanelStyles = new()
    {
        ["big_bar"]       = new(Stretched: "big_bar.png"),
        ["info_window"]   = new(Texture: "info_window.png",             NpMargins: new Vector4(16, 16, 16, 16)),
        ["info_window_2"] = new(Texture: "info_window_2.png",           NpMargins: new Vector4(16, 16, 16, 16)),
        ["dark_card"]     = new(Texture: "little_background_frame.png", NpMargins: new Vector4(10, 10, 10, 10)),
        ["dialoge"]       = new(Texture: "dialoge_frame.png",           NpMargins: new Vector4(16, 16, 16, 16)),
        ["inventory"]     = new(Texture: "inventory_frame.png",         NpMargins: new Vector4(16, 16, 16, 16)),
        ["skill"]         = new(Texture: "skill_frame.png",             NpMargins: new Vector4(12, 12, 12, 12)),
        ["thin"]          = new(Texture: "Thin_frame.png",              NpMargins: new Vector4(8, 8, 8, 8)),
        ["red_vert"]      = new(Texture: "Red_vert_panel.png",          NpMargins: new Vector4(12, 12, 12, 12)),
    };

    // =========================================================================
    // CHECKBOX STYLES
    // =========================================================================

    public static readonly Dictionary<string, CheckboxStyleData> CheckboxStyles = new()
    {
        ["default"]            = new("Round_fr.png",                  "update/Background_r_green.png"),
        ["glow_round_blue"]    = new("update/RoundFrame_blue.png",    "update/Background_r_blue.png"),
        ["glow_round_green"]   = new("update/RoundFrame_green.png",   "update/Background_r_green.png"),
        ["glow_round_grey"]    = new("update/RoundFrame_grey.png",    "update/Background_r_grey.png"),
        ["glow_round_purple"]  = new("update/RoundFrame_purple.png",  "update/Background_r_purple.png"),
        ["glow_round_red"]     = new("update/RoundFrame_red.png",     "update/Background_r_red.png"),
        ["glow_square_blue"]   = new("update/Frame_blue.png",         "update/Background_blue.png"),
        ["glow_square_green"]  = new("update/Frame_green.png",        "update/Background_green.png"),
        ["glow_square_grey"]   = new("update/Frame_grey.png",         "update/Background_grey.png"),
        ["glow_square_purple"] = new("update/Frame_purple.png",       "update/Background_purple.png"),
        ["glow_square_red"]    = new("update/Frame_red.png",          "update/Background_red.png"),
    };

    // =========================================================================
    // MINI BUTTONS DATA
    // =========================================================================

    public static readonly Dictionary<string, MiniButtonData> MiniButtons = new()
    {
        ["exit"]         = new("Mini_exit.png",        "Mini_exit_t.png"),
        ["add"]          = new("Mini_add.png",         "Mini_add_t.png"),
        ["help"]         = new("Mini_help.png",        "Mini_help_t.png"),
        ["guild"]        = new("Mini_guild.png",       "Mini_guild_t.png"),
        ["skip"]         = new("Mini_skip.png",        "Mini_skip_t.png"),
        ["speak"]        = new("Mini_speak.png",       "Mini_speak_t.png"),
        ["arrow_top"]    = new("Mini_arrow_top.png",   "Mini_arrow_top.png"),
        ["arrow_bot"]    = new("Mini_arrow_bot.png",   "Mini_arrow_bot.png"),
        ["arrow_top2"]   = new("Mini_arrow_top2.png",  "Mini_arrow_top2_t.png"),
        ["arrow_bot2"]   = new("Mini_arrow_bot2.png",  "Mini_arrow_bot2_t.png"),
        ["arrow_left2"]  = new("Mini_arrow_left2.png", "Mini_arrow_left2_t.png"),
        ["arrow_right2"] = new("Mini_arrow_right2.png","Mini_arrow_right2_t.png"),
    };

    // =========================================================================
    // ASSET CATALOG
    // =========================================================================

    public static readonly Dictionary<string, Dictionary<string, string>> Assets = new()
    {
        ["buttons"] = new()
        {
            ["long_normal"] = "long_button.png",     ["long_hover"] = "long_button_on.png",
            ["long_disabled"] = "long_button_off.png",["mid_normal"] = "mid_button.png",
            ["mid_hover"] = "mid_button_on.png",     ["mid_disabled"] = "mid_button_off.png",
            ["mid_vert"] = "mid_button_vert.png",    ["mini"] = "mini_button.png",
            ["inventory"] = "inventory_button.png",  ["inventory2"] = "inventory_button2.png",
            ["inventory2_long"] = "inventory_button2_long.png",
            ["option"] = "option_button.png",        ["plus"] = "Plus.png", ["minus"] = "Minus.png",
        },
        ["frames"] = new()
        {
            ["info_window"] = "info_window.png",           ["info_window_2"] = "info_window_2.png",
            ["dialoge"] = "dialoge_frame.png",             ["inventory"] = "inventory_frame.png",
            ["inventory_little"] = "inventory_frame_little.png",
            ["inventory_little_long"] = "inventory_frame_little_long.png",
            ["inventory_little_long_r"] = "inventory_frame_little_long_r.png",
            ["inventory_little_off"] = "inventory_frame_little_off.png",
            ["inventory_little_ready"] = "inventory_frame_little_ready.png",
            ["inventory_little_vert"] = "inventory_frame_little_vert.png",
            ["name"] = "name_frame.png",                   ["name_ready"] = "name_frame_ready.png",
            ["name_mid_ready"] = "name_frame_mid_ready.png",
            ["longbutton"] = "longbutton_frame.png",       ["midbutton"] = "midbutton_frame.png",
            ["scroll"] = "scroll_frame.png",               ["skill"] = "skill_frame.png",
            ["thin"] = "Thin_frame.png",                   ["round"] = "Round_fr.png",
            ["little_round"] = "little_round_frame.png",   ["little_round2"] = "little_round_frame2.png",
            ["little_round2_elite"] = "little_round_frame2_elite.png",
            ["little_background"] = "little_background_frame.png",
            ["long_loading"] = "long_frame_loading.png",   ["update_empty"] = "update/Frame_empty.png",
        },
        ["bars"] = new()
        {
            ["big_bar"] = "big_bar.png",             ["big_bar_bg"] = "big_bar_bg.png",
            ["big_bar_frame"] = "big_bar_frame.png", ["basic"] = "basic_bar.png",
            ["basic2"] = "basic_bar2.png",           ["basic2_elite"] = "basic_bar2_elite.png",
            ["mid"] = "mid_bar.png",                 ["mid_frame"] = "mid_bar_frame.png",
            ["lil"] = "lil_bar.png",                 ["inventory"] = "Inventory_bar.png",
            ["skill"] = "skill_bar.png",             ["skill_bg"] = "skill_bar_background.png",
            ["red_vert_panel"] = "Red_vert_panel.png",
        },
        ["lines"] = new()
        {
            ["hp"] = "Hp_line.png",         ["hp2"] = "Hp_line2.png",       ["hp_frame"] = "Hp_frame.png",
            ["mana"] = "Mana_line.png",     ["mana2"] = "Mana_line2.png",
            ["energy"] = "energy_line.png", ["energy2"] = "energy_line2.png",
            ["venom"] = "Venom_line.png",   ["xp"] = "xp_line.png",
            ["xp_bar"] = "xp_bar.png",     ["xp_bg"] = "xp_background.png",
            ["long"] = "long_line.png",     ["long2"] = "long_line2.png",
            ["long2_elite"] = "long_line2_elite.png", ["option"] = "option_line.png",
        },
        ["portrait"] = new()
        {
            ["frame"] = "Hero_icon_frame.png",       ["frame_bg"] = "Hero_icon_frame_bg.png",
            ["frame_elite"] = "Hero_icon_frame_elite.png", ["frame_rare"] = "Hero_icon_frame_rare.png",
        },
        ["silhouettes"] = new()
        {
            ["warrior_man"] = "Silhouette/warrior_silhouette_man.png",
            ["warrior_woman"] = "Silhouette/warrior_silhouette_woman.png",
            ["mage_man"] = "Silhouette/Mage_silhouette_man.png",
            ["mage_woman"] = "Silhouette/Mage_silhouette_woman.png",
            ["archer_man"] = "Silhouette/archer_silhouette_man.png",
            ["archer_woman"] = "Silhouette/archer_silhouette_woman.png",
            ["assassin_man"] = "Silhouette/assassin_silhouette_man.png",
            ["assassin_woman"] = "Silhouette/assassin_silhouette_woman.png",
            ["berserk_man"] = "Silhouette/berserk_silhouette_man.png",
            ["berserk_woman"] = "Silhouette/berserk_silhouette_woman.png",
            ["druid_man"] = "Silhouette/druid_silhouette_man.png",
            ["druid_woman"] = "Silhouette/druid_silhouette_woman.png",
            ["shadowmage_man"] = "Silhouette/shadowmage_silhouette_man.png",
            ["shadowmage_woman"] = "Silhouette/shadowmage_silhouette_woman.png",
        },
        ["icons"] = new()
        {
            ["bag"] = "Icons/Bag.png",         ["equip"] = "Icons/Equip.png",
            ["fight"] = "Icons/Fight.png",     ["honor"] = "Icons/Honor.png",
            ["inventory"] = "Icons/Inventory.png", ["menu"] = "Icons/Menu.png",
            ["options"] = "Icons/Options.png", ["profession"] = "Icons/Profession.png",
            ["quest"] = "Icons/Quest.png",     ["scull"] = "Icons/Scull.png",
            ["scull2"] = "Icons/Scull2.png",   ["exit"] = "Icons/exit.png",
            ["greed"] = "Icons/greed.png",     ["need"] = "Icons/need.png",
            ["runes"] = "Icons/runes.png",     ["skills"] = "Icons/skills.png",
            ["trade"] = "Icons/trade.png",
        },
        ["slot_backgrounds"] = new()
        {
            ["rune"] = "frame_backgrounds/Rune_background.png",
            ["arrows"] = "frame_backgrounds/arrows_background.png",
            ["back"] = "frame_backgrounds/back_background.png",
            ["boots"] = "frame_backgrounds/boots_background.png",
            ["bracers"] = "frame_backgrounds/bracers_background.png",
            ["chest"] = "frame_backgrounds/chest_background.png",
            ["gloves"] = "frame_backgrounds/gloves_background.png",
            ["head"] = "frame_backgrounds/head_background.png",
            ["helm"] = "frame_backgrounds/helm_background.png",
            ["lock"] = "frame_backgrounds/lock_background.png",
            ["melee"] = "frame_backgrounds/melee_background.png",
            ["neck"] = "frame_backgrounds/neck_background.png",
            ["pants"] = "frame_backgrounds/pants_background.png",
            ["potion"] = "frame_backgrounds/potion_background.png",
            ["range"] = "frame_backgrounds/range_background.png",
            ["ring"] = "frame_backgrounds/ring_background.png",
            ["shield"] = "frame_backgrounds/shield_background.png",
            ["shoulder"] = "frame_backgrounds/shoulder_background.png",
            ["skill"] = "frame_backgrounds/skill_background.png",
            ["trinket"] = "frame_backgrounds/trinket_background.png",
            ["wand"] = "frame_backgrounds/wand_background.png",
        },
        ["backgrounds"] = new()
        {
            ["paper_01"] = "Paper_01.png", ["paper_02"] = "Paper_02.png",
            ["pattern"] = "pattern.png",   ["minimap"] = "minimap.png",
        },
        ["special_items"] = new()
        {
            ["book"] = "update/Book.png",        ["destruction"] = "update/Destruction.png",
            ["guitar"] = "update/Guitar.png",    ["ink"] = "update/Ink.png",
        },
    };
}
