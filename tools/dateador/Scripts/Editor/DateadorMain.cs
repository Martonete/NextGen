#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AODateador.Data;

// ════════════════════════════════════════════════════════════════════════════
// DateadorMain — main orchestrator; builds entire UI programmatically.
// Attached to Main.tscn (Control node).
// ════════════════════════════════════════════════════════════════════════════
namespace AODateador.Editor
{
    public partial class DateadorMain : Control
    {
        // ── Dropdown option arrays ────────────────────────────────────────────

        private static readonly string[] NpcTypeNames =
        {
            "Común", "Resucitador", "Guardia Real", "Entrenador", "Banquero",
            "Noble", "Dragón", "Apostador", "Guardia Caos", "Renuncia",
            "Rey Castillo", "Quest", "Viajero", "Ciudadanía", "Inscribir",
            "Vende Casas", "Arena", "Quest Noble", "NPC Dios", "Cirujano",
        };

        private static readonly string[] ObjTypeNames =
        {
            "(ninguno)", "UseOnce", "Weapon", "Armor", "Trees", "Money",
            "Door", "Container", "Sign", "Key", "Forum", "Potion",
            "(12)", "Drink", "(14)", "(15)", "Shield", "Helmet", "Tool",
            "Teleport", "(20)", "(21)", "(22)", "(23)", "Scroll", "(25)",
            "Instrument", "(27)", "(28)", "(29)", "(30)", "Boat", "Arrow",
            "(33)", "(34)", "(35)", "(36)", "(37)", "(38)", "(39)", "(40)",
            "(41)", "(42)", "(43)", "(44)", "(45)", "(46)", "(47)", "Mount",
        };

        private static readonly string[] SpellTypeNames =
            { "(ninguno)", "Propiedades", "Estado", "Materialización", "Invocación", "Teleport" };

        private static readonly string[] SpellTargetNames =
            { "(ninguno)", "Solo Usuario", "Solo NPC", "Usuario y NPC", "Terreno", "Propio" };

        // ── State ─────────────────────────────────────────────────────────────

        private string         _datDir = string.Empty;
        private bool           _dirty  = false;

        private NpcDatabase?   _npcDb;
        private ObjDatabase?   _objDb;
        private SpellDatabase? _spellDb;
        private ExpTable?      _expTable;
        private CraftingData?  _craftData;

        // ── NPC tab ────────────────────────────────────────────────────────────
        private EntityListPanel? _npcList;
        private NpcData?         _currentNpc;

        private LineEdit?    _npcName, _npcDesc;
        private OptionButton? _npcTypeOb;
        private SpinBox?     _npcIndex;
        private SpinBox?     _npcHead, _npcBody, _npcHeading, _npcWeaponAnim, _npcShieldAnim, _npcCascoAnim;
        private SpinBox?     _npcMovement;
        private CheckBox?    _npcAttackable, _npcHostile, _npcRespawn;
        private SpinBox?     _npcDomable;
        private CheckBox?    _npcComercia;
        private SpinBox?     _npcMinHP, _npcMaxHP, _npcMinHit, _npcMaxHit, _npcDef, _npcDefM;
        private SpinBox?     _npcPoderAtaque, _npcPoderEvasion;
        private SpinBox?     _npcGiveEXP, _npcGiveGLD, _npcGiveGLDMin, _npcGiveGLDMax, _npcInflacion;
        private SpinBox?     _npcSND1, _npcSND2, _npcSND3;
        private bool         _npcSuppressEvents = false;

        // ── Object tab ────────────────────────────────────────────────────────
        private EntityListPanel? _objList;
        private ObjData?         _currentObj;

        private LineEdit?    _objName;
        private OptionButton? _objTypeOb;
        private SpinBox?     _objGrh, _objValor;
        private CheckBox?    _objAgarrable;
        // Weapon
        private SpinBox?  _objMinHIT, _objMaxHIT, _objWeaponAnim, _objMunicion;
        private CheckBox? _objDosManos, _objProyectil;
        // Defense
        private SpinBox?  _objMinDef, _objMaxDef, _objShieldAnim, _objCascoAnim, _objNumRopaje;
        // Potion
        private SpinBox?  _objTipoPocion, _objMinMod, _objMaxMod, _objDuracion;
        // Door   (Cerrada is int in ObjData)
        private SpinBox?  _objLlave, _objCerrada, _objIndexAbierta, _objIndexCerrada;
        // Smithing
        private SpinBox?  _objSkHerr, _objSkCarp, _objLingH, _objLingO, _objLingP, _objMadera;
        // Restrictions
        private CheckBox? _objReal, _objCaos, _objNewbie;
        private SpinBox?  _objLvl;
        // Spell / Sound
        private SpinBox?  _objHechIndex, _objSND1;
        private bool      _objSuppressEvents = false;

        // ── Spell tab ─────────────────────────────────────────────────────────
        private EntityListPanel? _spellList;
        private SpellData?       _currentSpell;

        private LineEdit?    _spNombre, _spDesc, _spPalabras;
        private LineEdit?    _spHechMsg, _spTargetMsg, _spPropioMsg;
        private OptionButton? _spTipo, _spTarget;
        private SpinBox?     _spMinSkill, _spMana, _spSta;
        private SpinBox?     _spSubeHP, _spMinHP, _spMaxHP;
        private SpinBox?     _spSubeMana, _spMinMana, _spMaxMana;
        private SpinBox?     _spSubeSta, _spMinSta, _spMaxSta;
        private CheckBox?    _spInvis, _spParaliza, _spInmoviliza, _spEnvenena, _spMaldicion;
        private CheckBox?    _spBendicion, _spCuraVeneno, _spRemPar, _spRevivir, _spEstupidez;
        private CheckBox?    _spCeguera, _spMimetiza, _spRemMald, _spRemEst;
        private SpinBox?     _spWAV, _spFXGrh, _spLoops;
        private bool         _spSuppressEvents = false;

        // ── Exp tab ───────────────────────────────────────────────────────────
        private SpinBox?[] _expFields = Array.Empty<SpinBox?>();

        // ── Crafting tab ──────────────────────────────────────────────────────
        private ItemList? _craftSmithWeaponList, _craftSmithArmorList, _craftCarpList;

        // ── Layout ────────────────────────────────────────────────────────────
        private TabBar?    _tabBar;
        private Control?[] _tabPanels = Array.Empty<Control?>();
        private Label?     _statusLabel;
        private Label?     _dirtyLabel;

        // ═════════════════════════════════════════════════════════════════════
        // _Ready
        // ═════════════════════════════════════════════════════════════════════

        public override void _Ready()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            BuildUI();
            SetStatus("Bienvenido — abrí una carpeta dat para comenzar.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // BuildUI
        // ═════════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            var root = new VBoxContainer();
            root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            root.AddThemeConstantOverride("separation", 0);
            AddChild(root);

            root.AddChild(BuildMenuBar());
            root.AddChild(DateadorTheme.Separator());

            // ── Tab bar ───────────────────────────────────────────────────────
            _tabBar = new TabBar
            {
                TabAlignment        = TabBar.AlignmentMode.Left,
                ClipTabs            = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _tabBar.AddThemeStyleboxOverride("tab_unselected",
                DateadorTheme.FlatBox(DateadorTheme.BG_TAB,     4, 14, 6));
            _tabBar.AddThemeStyleboxOverride("tab_selected",
                DateadorTheme.FlatBox(DateadorTheme.BG_TAB_SEL, 4, 14, 6));
            _tabBar.AddThemeFontSizeOverride("font_size", DateadorTheme.FONT_MD);
            _tabBar.AddThemeColorOverride("font_selected_color",   DateadorTheme.TEXT_PRI);
            _tabBar.AddThemeColorOverride("font_unselected_color", DateadorTheme.TEXT_SEC);

            string[] tabNames = { "NPCs", "Objetos", "Hechizos", "Experiencia", "Recetas" };
            foreach (var name in tabNames) _tabBar.AddTab(name);
            _tabBar.TabChanged += OnTabChanged;
            root.AddChild(_tabBar);

            // ── Content area (stacked panels) ─────────────────────────────────
            var contentArea = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            contentArea.AddThemeStyleboxOverride("panel",
                DateadorTheme.FlatBox(DateadorTheme.BG_PANEL, 0, 0, 0));
            root.AddChild(contentArea);

            _tabPanels = new Control?[tabNames.Length];

            var stack = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
            stack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            contentArea.AddChild(stack);

            _tabPanels[0] = BuildNpcTab();
            _tabPanels[1] = BuildObjTab();
            _tabPanels[2] = BuildSpellTab();
            _tabPanels[3] = BuildExpTab();
            _tabPanels[4] = BuildCraftingTab();

            foreach (var panel in _tabPanels)
            {
                if (panel == null) continue;
                panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                panel.Visible = false;
                stack.AddChild(panel);
            }
            _tabPanels[0]!.Visible = true;

            // ── Status bar ────────────────────────────────────────────────────
            var statusBar = new HBoxContainer();
            statusBar.AddThemeConstantOverride("separation", 8);

            _statusLabel = DateadorTheme.MakeLabel(string.Empty, DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
            _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            statusBar.AddChild(_statusLabel);

            _dirtyLabel = DateadorTheme.MakeLabel(string.Empty, DateadorTheme.DIRTY_RED, DateadorTheme.FONT_SM);
            statusBar.AddChild(_dirtyLabel);

            root.AddChild(statusBar);
        }

        // ── Menu bar ──────────────────────────────────────────────────────────

        private HBoxContainer BuildMenuBar()
        {
            var bar = new HBoxContainer();
            bar.AddThemeConstantOverride("separation", 0);

            var menuBtn = new MenuButton { Text = "  Archivo  ", Flat = true };
            menuBtn.AddThemeFontSizeOverride("font_size", DateadorTheme.FONT_MD);
            menuBtn.AddThemeColorOverride("font_color", DateadorTheme.TEXT_PRI);

            var popup = menuBtn.GetPopup();
            popup.AddItem("Abrir carpeta dat…", 0);
            popup.AddSeparator();
            popup.AddItem("Guardar todo",        1);
            popup.AddSeparator();
            popup.AddItem("Salir",               2);
            popup.IdPressed += OnMenuIdPressed;
            bar.AddChild(menuBtn);

            var title = DateadorTheme.MakeLabel(
                "  Argentum Online — Dateador",
                DateadorTheme.TEXT_DIM, DateadorTheme.FONT_SM);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            title.HorizontalAlignment = HorizontalAlignment.Right;
            bar.AddChild(title);

            return bar;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Tab panels
        // ═════════════════════════════════════════════════════════════════════

        private Control BuildNpcTab()
        {
            var split = new HSplitContainer { SplitOffset = 280 };
            split.AddThemeConstantOverride("separation", 4);

            _npcList = new EntityListPanel();
            _npcList.ItemSelected += OnNpcSelected;
            _npcList.AddRequested += OnNpcAdd;
            split.AddChild(_npcList);

            var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddThemeStyleboxOverride("panel",
                DateadorTheme.FlatBox(DateadorTheme.BG_PANEL, 0, 0, 0));

            var props = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            props.AddThemeConstantOverride("separation", 6);

            // Identidad
            AddSection(props, "Identidad");
            _npcName   = DateadorTheme.MakeLineEdit("Nombre del NPC");
            _npcDesc   = DateadorTheme.MakeLineEdit("Descripción");
            _npcTypeOb = DateadorTheme.MakeOptionButton(NpcTypeNames);
            _npcIndex  = DateadorTheme.MakeSpinBox(0, 9999, 1, 120);
            AddField(props, "Nombre",   _npcName);
            AddField(props, "Desc",     _npcDesc);
            AddField(props, "Tipo NPC", _npcTypeOb);
            AddField(props, "Index",    _npcIndex);

            // Apariencia
            AddSection(props, "Apariencia");
            _npcHead       = DateadorTheme.MakeSpinBox(0, 9999);
            _npcBody       = DateadorTheme.MakeSpinBox(0, 9999);
            _npcHeading    = DateadorTheme.MakeSpinBox(0, 8);
            _npcWeaponAnim = DateadorTheme.MakeSpinBox(0, 9999);
            _npcShieldAnim = DateadorTheme.MakeSpinBox(0, 9999);
            _npcCascoAnim  = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "Head",       _npcHead);
            AddField(props, "Body",       _npcBody);
            AddField(props, "Heading",    _npcHeading);
            AddField(props, "WeaponAnim", _npcWeaponAnim);
            AddField(props, "ShieldAnim", _npcShieldAnim);
            AddField(props, "CascoAnim",  _npcCascoAnim);

            // Comportamiento
            AddSection(props, "Comportamiento");
            _npcMovement   = DateadorTheme.MakeSpinBox(0, 5);
            _npcAttackable = DateadorTheme.MakeCheckBox("Attackable");
            _npcHostile    = DateadorTheme.MakeCheckBox("Hostile");
            _npcRespawn    = DateadorTheme.MakeCheckBox("Respawn");
            _npcDomable    = DateadorTheme.MakeSpinBox(0, 9999);
            _npcComercia   = DateadorTheme.MakeCheckBox("Comercia");
            AddField(props, "Movement",   _npcMovement);
            AddField(props, "Attackable", _npcAttackable);
            AddField(props, "Hostile",    _npcHostile);
            AddField(props, "Respawn",    _npcRespawn);
            AddField(props, "Domable",    _npcDomable);
            AddField(props, "Comercia",   _npcComercia);

            // Combate
            AddSection(props, "Combate");
            _npcMinHP        = DateadorTheme.MakeSpinBox(0, 999999);
            _npcMaxHP        = DateadorTheme.MakeSpinBox(0, 999999);
            _npcMinHit       = DateadorTheme.MakeSpinBox(0, 9999);
            _npcMaxHit       = DateadorTheme.MakeSpinBox(0, 9999);
            _npcDef          = DateadorTheme.MakeSpinBox(0, 9999);
            _npcDefM         = DateadorTheme.MakeSpinBox(0, 9999);
            _npcPoderAtaque  = DateadorTheme.MakeSpinBox(0, 9999);
            _npcPoderEvasion = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "MinHP",        _npcMinHP);
            AddField(props, "MaxHP",        _npcMaxHP);
            AddField(props, "MinHit",       _npcMinHit);
            AddField(props, "MaxHit",       _npcMaxHit);
            AddField(props, "Def",          _npcDef);
            AddField(props, "DefM",         _npcDefM);
            AddField(props, "PoderAtaque",  _npcPoderAtaque);
            AddField(props, "PoderEvasion", _npcPoderEvasion);

            // Economía
            AddSection(props, "Economía");
            _npcGiveEXP    = DateadorTheme.MakeSpinBox(0, 9999999);
            _npcGiveGLD    = DateadorTheme.MakeSpinBox(0, 9999999);
            _npcGiveGLDMin = DateadorTheme.MakeSpinBox(0, 9999999);
            _npcGiveGLDMax = DateadorTheme.MakeSpinBox(0, 9999999);
            _npcInflacion  = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "GiveEXP",    _npcGiveEXP);
            AddField(props, "GiveGLD",    _npcGiveGLD);
            AddField(props, "GiveGLDMin", _npcGiveGLDMin);
            AddField(props, "GiveGLDMax", _npcGiveGLDMax);
            AddField(props, "Inflacion",  _npcInflacion);

            // Sonido
            AddSection(props, "Sonido");
            _npcSND1 = DateadorTheme.MakeSpinBox(0, 9999);
            _npcSND2 = DateadorTheme.MakeSpinBox(0, 9999);
            _npcSND3 = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "SND1", _npcSND1);
            AddField(props, "SND2", _npcSND2);
            AddField(props, "SND3", _npcSND3);

            WireNpcEvents();

            scroll.AddChild(props);
            split.AddChild(scroll);
            return split;
        }

        private Control BuildObjTab()
        {
            var split = new HSplitContainer { SplitOffset = 280 };
            split.AddThemeConstantOverride("separation", 4);

            _objList = new EntityListPanel();
            _objList.ItemSelected += OnObjSelected;
            _objList.AddRequested += OnObjAdd;
            split.AddChild(_objList);

            var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddThemeStyleboxOverride("panel",
                DateadorTheme.FlatBox(DateadorTheme.BG_PANEL, 0, 0, 0));

            var props = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            props.AddThemeConstantOverride("separation", 6);

            // General
            AddSection(props, "General");
            _objName      = DateadorTheme.MakeLineEdit("Nombre del objeto");
            _objTypeOb    = DateadorTheme.MakeOptionButton(ObjTypeNames);
            _objGrh       = DateadorTheme.MakeSpinBox(0, 99999);
            _objValor     = DateadorTheme.MakeSpinBox(0, 9999999);
            _objAgarrable = DateadorTheme.MakeCheckBox("Agarrable");
            AddField(props, "Name",      _objName);
            AddField(props, "ObjType",   _objTypeOb);
            AddField(props, "GrhIndex",  _objGrh);
            AddField(props, "Valor",     _objValor);
            AddField(props, "Agarrable", _objAgarrable);

            // Arma
            AddSection(props, "Arma");
            _objMinHIT     = DateadorTheme.MakeSpinBox(0, 9999);
            _objMaxHIT     = DateadorTheme.MakeSpinBox(0, 9999);
            _objWeaponAnim = DateadorTheme.MakeSpinBox(0, 9999);
            _objDosManos   = DateadorTheme.MakeCheckBox("DosManos");
            _objProyectil  = DateadorTheme.MakeCheckBox("Proyectil");   // bool in ObjData
            _objMunicion   = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "MinHIT",     _objMinHIT);
            AddField(props, "MaxHIT",     _objMaxHIT);
            AddField(props, "WeaponAnim", _objWeaponAnim);
            AddField(props, "DosManos",   _objDosManos);
            AddField(props, "Proyectil",  _objProyectil);
            AddField(props, "Municion",   _objMunicion);

            // Defensa
            AddSection(props, "Defensa");
            _objMinDef     = DateadorTheme.MakeSpinBox(0, 9999);
            _objMaxDef     = DateadorTheme.MakeSpinBox(0, 9999);
            _objShieldAnim = DateadorTheme.MakeSpinBox(0, 9999);
            _objCascoAnim  = DateadorTheme.MakeSpinBox(0, 9999);
            _objNumRopaje  = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "MinDef",     _objMinDef);
            AddField(props, "MaxDef",     _objMaxDef);
            AddField(props, "ShieldAnim", _objShieldAnim);
            AddField(props, "CascoAnim",  _objCascoAnim);
            AddField(props, "NumRopaje",  _objNumRopaje);

            // Pociones
            AddSection(props, "Pociones");
            _objTipoPocion = DateadorTheme.MakeSpinBox(0, 99);
            _objMinMod     = DateadorTheme.MakeSpinBox(0, 9999);
            _objMaxMod     = DateadorTheme.MakeSpinBox(0, 9999);
            _objDuracion   = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "TipoPocion",     _objTipoPocion);
            AddField(props, "MinModificador", _objMinMod);
            AddField(props, "MaxModificador", _objMaxMod);
            AddField(props, "DuracionEfecto", _objDuracion);

            // Puerta  (Cerrada is int: 0=open, 1=closed)
            AddSection(props, "Puerta");
            _objLlave        = DateadorTheme.MakeSpinBox(0, 9999);
            _objCerrada      = DateadorTheme.MakeSpinBox(0, 1);     // int field
            _objIndexAbierta = DateadorTheme.MakeSpinBox(0, 9999);
            _objIndexCerrada = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "Llave",        _objLlave);
            AddField(props, "Cerrada",      _objCerrada);
            AddField(props, "IndexAbierta", _objIndexAbierta);
            AddField(props, "IndexCerrada", _objIndexCerrada);

            // Herrería / Carpintería
            AddSection(props, "Herrería / Carpintería");
            _objSkHerr = DateadorTheme.MakeSpinBox(0, 100);
            _objSkCarp = DateadorTheme.MakeSpinBox(0, 100);
            _objLingH  = DateadorTheme.MakeSpinBox(0, 999);
            _objLingO  = DateadorTheme.MakeSpinBox(0, 999);
            _objLingP  = DateadorTheme.MakeSpinBox(0, 999);
            _objMadera = DateadorTheme.MakeSpinBox(0, 999);
            AddField(props, "SkHerreria",    _objSkHerr);
            AddField(props, "SkCarpinteria", _objSkCarp);
            AddField(props, "LingH",         _objLingH);
            AddField(props, "LingO",         _objLingO);
            AddField(props, "LingP",         _objLingP);
            AddField(props, "Madera",        _objMadera);

            // Restricciones
            AddSection(props, "Restricciones");
            _objReal   = DateadorTheme.MakeCheckBox("Solo Real");
            _objCaos   = DateadorTheme.MakeCheckBox("Solo Caos");
            _objNewbie = DateadorTheme.MakeCheckBox("Newbie");
            _objLvl    = DateadorTheme.MakeSpinBox(0, 999);
            AddField(props, "Real",   _objReal);
            AddField(props, "Caos",   _objCaos);
            AddField(props, "Newbie", _objNewbie);
            AddField(props, "Lvl",    _objLvl);

            // Hechizo / Sonido
            AddSection(props, "Hechizo / Sonido");
            _objHechIndex = DateadorTheme.MakeSpinBox(0, 9999);
            _objSND1      = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "HechIndex", _objHechIndex);
            AddField(props, "SND1",      _objSND1);

            WireObjEvents();

            scroll.AddChild(props);
            split.AddChild(scroll);
            return split;
        }

        private Control BuildSpellTab()
        {
            var split = new HSplitContainer { SplitOffset = 280 };
            split.AddThemeConstantOverride("separation", 4);

            _spellList = new EntityListPanel();
            _spellList.ItemSelected += OnSpellSelected;
            _spellList.AddRequested += OnSpellAdd;
            split.AddChild(_spellList);

            var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddThemeStyleboxOverride("panel",
                DateadorTheme.FlatBox(DateadorTheme.BG_PANEL, 0, 0, 0));

            var props = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            props.AddThemeConstantOverride("separation", 6);

            // Identidad
            AddSection(props, "Identidad");
            _spNombre   = DateadorTheme.MakeLineEdit("Nombre del hechizo");
            _spDesc     = DateadorTheme.MakeLineEdit("Descripción");
            _spPalabras = DateadorTheme.MakeLineEdit("Palabras mágicas");
            AddField(props, "Nombre",          _spNombre);
            AddField(props, "Desc",            _spDesc);
            AddField(props, "PalabrasMagicas", _spPalabras);

            // Mensajes
            AddSection(props, "Mensajes");
            _spHechMsg   = DateadorTheme.MakeLineEdit("Mensaje al lanzar");
            _spTargetMsg = DateadorTheme.MakeLineEdit("Mensaje al objetivo");
            _spPropioMsg = DateadorTheme.MakeLineEdit("Mensaje propio");
            AddField(props, "HechizeroMsg", _spHechMsg);
            AddField(props, "TargetMsg",    _spTargetMsg);
            AddField(props, "PropioMsg",    _spPropioMsg);

            // Tipo
            AddSection(props, "Tipo");
            _spTipo   = DateadorTheme.MakeOptionButton(SpellTypeNames);
            _spTarget = DateadorTheme.MakeOptionButton(SpellTargetNames);
            AddField(props, "Tipo",   _spTipo);
            AddField(props, "Target", _spTarget);

            // Requisitos
            AddSection(props, "Requisitos");
            _spMinSkill = DateadorTheme.MakeSpinBox(0, 100);
            _spMana     = DateadorTheme.MakeSpinBox(0, 9999);
            _spSta      = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "MinSkill",      _spMinSkill);
            AddField(props, "ManaRequerido", _spMana);
            AddField(props, "StaRequerido",  _spSta);

            // Efectos HP / Mana / Stamina
            AddSection(props, "Efectos HP / Mana / Stamina");
            _spSubeHP   = DateadorTheme.MakeSpinBox(0, 2);
            _spMinHP    = DateadorTheme.MakeSpinBox(0, 9999);
            _spMaxHP    = DateadorTheme.MakeSpinBox(0, 9999);
            _spSubeMana = DateadorTheme.MakeSpinBox(0, 2);
            _spMinMana  = DateadorTheme.MakeSpinBox(0, 9999);
            _spMaxMana  = DateadorTheme.MakeSpinBox(0, 9999);
            _spSubeSta  = DateadorTheme.MakeSpinBox(0, 2);
            _spMinSta   = DateadorTheme.MakeSpinBox(0, 9999);
            _spMaxSta   = DateadorTheme.MakeSpinBox(0, 9999);
            AddField(props, "SubeHP",   _spSubeHP);
            AddField(props, "MinHP",    _spMinHP);
            AddField(props, "MaxHP",    _spMaxHP);
            AddField(props, "SubeMana", _spSubeMana);
            AddField(props, "MinMana",  _spMinMana);
            AddField(props, "MaxMana",  _spMaxMana);
            AddField(props, "SubeSta",  _spSubeSta);
            AddField(props, "MinSta",   _spMinSta);
            AddField(props, "MaxSta",   _spMaxSta);

            // Estados (bool flags — laid out in rows of 3–4)
            AddSection(props, "Estados");
            _spInvis      = DateadorTheme.MakeCheckBox("Invisibilidad");
            _spParaliza   = DateadorTheme.MakeCheckBox("Paraliza");
            _spInmoviliza = DateadorTheme.MakeCheckBox("Inmoviliza");
            _spEnvenena   = DateadorTheme.MakeCheckBox("Envenena");
            _spMaldicion  = DateadorTheme.MakeCheckBox("Maldicion");
            _spBendicion  = DateadorTheme.MakeCheckBox("Bendicion");
            _spCuraVeneno = DateadorTheme.MakeCheckBox("CuraVeneno");
            _spRemPar     = DateadorTheme.MakeCheckBox("RemoverParalisis");
            _spRevivir    = DateadorTheme.MakeCheckBox("Revivir");
            _spEstupidez  = DateadorTheme.MakeCheckBox("Estupidez");
            _spCeguera    = DateadorTheme.MakeCheckBox("Ceguera");
            _spMimetiza   = DateadorTheme.MakeCheckBox("Mimetiza");
            _spRemMald    = DateadorTheme.MakeCheckBox("RemoverMaldicion");
            _spRemEst     = DateadorTheme.MakeCheckBox("RemoverEstupidez");

            var row1 = new HBoxContainer(); row1.AddThemeConstantOverride("separation", 16);
            var row2 = new HBoxContainer(); row2.AddThemeConstantOverride("separation", 16);
            var row3 = new HBoxContainer(); row3.AddThemeConstantOverride("separation", 16);
            var row4 = new HBoxContainer(); row4.AddThemeConstantOverride("separation", 16);

            row1.AddChild(_spInvis);    row1.AddChild(_spParaliza);   row1.AddChild(_spInmoviliza);
            row2.AddChild(_spEnvenena); row2.AddChild(_spMaldicion);  row2.AddChild(_spBendicion); row2.AddChild(_spCuraVeneno);
            row3.AddChild(_spRemPar);   row3.AddChild(_spRevivir);    row3.AddChild(_spEstupidez); row3.AddChild(_spCeguera);
            row4.AddChild(_spMimetiza); row4.AddChild(_spRemMald);    row4.AddChild(_spRemEst);

            props.AddChild(row1);
            props.AddChild(row2);
            props.AddChild(row3);
            props.AddChild(row4);

            // Visual / Sonido
            AddSection(props, "Visual / Sonido");
            _spWAV   = DateadorTheme.MakeSpinBox(0, 9999);
            _spFXGrh = DateadorTheme.MakeSpinBox(0, 9999);
            _spLoops = DateadorTheme.MakeSpinBox(0, 999);
            AddField(props, "WAV",   _spWAV);
            AddField(props, "FXGrh", _spFXGrh);
            AddField(props, "Loops", _spLoops);

            WireSpellEvents();

            scroll.AddChild(props);
            split.AddChild(scroll);
            return split;
        }

        private Control BuildExpTab()
        {
            var scroll = new ScrollContainer();
            scroll.AddThemeStyleboxOverride("panel",
                DateadorTheme.FlatBox(DateadorTheme.BG_PANEL, 0, 0, 0));

            var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            vbox.AddThemeConstantOverride("separation", 4);
            vbox.AddChild(DateadorTheme.Heading("Tabla de Experiencia por Nivel"));
            vbox.AddChild(DateadorTheme.Separator());

            _expFields = new SpinBox?[ExpTable.MaxLevel + 1];

            for (int lvl = 1; lvl <= ExpTable.MaxLevel; lvl++)
            {
                int capturedLvl = lvl;
                var sb = DateadorTheme.MakeSpinBox(0, 9_999_999_999_999.0, 1, 180);
                _expFields[lvl] = sb;
                sb.ValueChanged += _ =>
                {
                    if (_expTable == null) return;
                    _expTable.Levels[capturedLvl] = (long)sb.Value;
                    MarkDirty();
                };
                AddField(vbox, $"Nivel {capturedLvl}", sb);
            }

            scroll.AddChild(vbox);
            return scroll;
        }

        private Control BuildCraftingTab()
        {
            var vbox = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            vbox.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(DateadorTheme.Heading("Recetas de Crafting"));
            vbox.AddChild(DateadorTheme.Separator());

            var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            cols.AddThemeConstantOverride("separation", 8);

            cols.AddChild(BuildCraftColumn("Herrería — Armas", out _craftSmithWeaponList,
                () => OnCraftAdd(CraftListKind.SmithWeapon),
                () => OnCraftRemove(CraftListKind.SmithWeapon, _craftSmithWeaponList)));

            cols.AddChild(BuildCraftColumn("Herrería — Armaduras", out _craftSmithArmorList,
                () => OnCraftAdd(CraftListKind.SmithArmor),
                () => OnCraftRemove(CraftListKind.SmithArmor, _craftSmithArmorList)));

            cols.AddChild(BuildCraftColumn("Carpintería", out _craftCarpList,
                () => OnCraftAdd(CraftListKind.Carpenter),
                () => OnCraftRemove(CraftListKind.Carpenter, _craftCarpList)));

            vbox.AddChild(cols);
            return vbox;
        }

        private VBoxContainer BuildCraftColumn(
            string title, out ItemList listOut, Action addCb, Action removeCb)
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            col.AddThemeConstantOverride("separation", 4);
            col.AddChild(DateadorTheme.MakeLabel(title, DateadorTheme.ACCENT, DateadorTheme.FONT_MD));

            var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
            DateadorTheme.StyleItemList(list);
            col.AddChild(list);
            listOut = list;

            var btns = new HBoxContainer();
            btns.AddThemeConstantOverride("separation", 4);
            btns.AddChild(DateadorTheme.SecondaryButton("+ Agregar", addCb));
            btns.AddChild(DateadorTheme.SecondaryButton("− Quitar",  removeCb));
            col.AddChild(btns);

            return col;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Layout helpers
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Adds a colored section-header panel to <paramref name="parent"/>.</summary>
        private static void AddSection(VBoxContainer parent, string title)
        {
            parent.AddChild(DateadorTheme.Separator());

            var lbl = DateadorTheme.MakeLabel($"  {title}", DateadorTheme.ACCENT, DateadorTheme.FONT_MD);
            lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            panel.AddThemeStyleboxOverride("panel",
                DateadorTheme.FlatBox(DateadorTheme.BG_SECTION, 4, 0, 2, DateadorTheme.BORDER, 1));
            panel.AddChild(lbl);
            parent.AddChild(panel);
        }

        /// <summary>Adds a row: Label (min 180 px) + input (expand fill).</summary>
        private static void AddField(VBoxContainer parent, string label, Control input)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var lbl = DateadorTheme.MakeLabel(label, DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
            lbl.CustomMinimumSize   = new Vector2(180, 0);
            lbl.VerticalAlignment   = VerticalAlignment.Center;
            lbl.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

            input.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(lbl);
            row.AddChild(input);
            parent.AddChild(row);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Event wiring
        // ═════════════════════════════════════════════════════════════════════

        private void WireNpcEvents()
        {
            WireLE(_npcName,   () => { if (_currentNpc != null) _currentNpc.Name    = _npcName!.Text; });
            WireLE(_npcDesc,   () => { if (_currentNpc != null) _currentNpc.Desc    = _npcDesc!.Text; });
            WireOB(_npcTypeOb, () => { if (_currentNpc != null) _currentNpc.NpcType = _npcTypeOb!.Selected; });
            WireSB(_npcIndex,  v  => { if (_currentNpc != null) _currentNpc.Index   = (int)v; });

            WireSB(_npcHead,       v => { if (_currentNpc != null) _currentNpc.Head       = (int)v; });
            WireSB(_npcBody,       v => { if (_currentNpc != null) _currentNpc.Body       = (int)v; });
            WireSB(_npcHeading,    v => { if (_currentNpc != null) _currentNpc.Heading    = (int)v; });
            WireSB(_npcWeaponAnim, v => { if (_currentNpc != null) _currentNpc.WeaponAnim = (int)v; });
            WireSB(_npcShieldAnim, v => { if (_currentNpc != null) _currentNpc.ShieldAnim = (int)v; });
            WireSB(_npcCascoAnim,  v => { if (_currentNpc != null) _currentNpc.CascoAnim  = (int)v; });

            WireSB(_npcMovement,  v => { if (_currentNpc != null) _currentNpc.Movement  = (int)v; });
            WireCB(_npcAttackable,v => { if (_currentNpc != null) _currentNpc.Attackable = v; });
            WireCB(_npcHostile,   v => { if (_currentNpc != null) _currentNpc.Hostile   = v; });
            WireCB(_npcRespawn,   v => { if (_currentNpc != null) _currentNpc.Respawn   = v; });
            WireSB(_npcDomable,   v => { if (_currentNpc != null) _currentNpc.Domable   = (int)v; });
            WireCB(_npcComercia,  v => { if (_currentNpc != null) _currentNpc.Comercia  = v; });

            WireSB(_npcMinHP,        v => { if (_currentNpc != null) _currentNpc.MinHP        = (int)v; });
            WireSB(_npcMaxHP,        v => { if (_currentNpc != null) _currentNpc.MaxHP        = (int)v; });
            WireSB(_npcMinHit,       v => { if (_currentNpc != null) _currentNpc.MinHit       = (int)v; });
            WireSB(_npcMaxHit,       v => { if (_currentNpc != null) _currentNpc.MaxHit       = (int)v; });
            WireSB(_npcDef,          v => { if (_currentNpc != null) _currentNpc.Def          = (int)v; });
            WireSB(_npcDefM,         v => { if (_currentNpc != null) _currentNpc.DefM         = (int)v; });
            WireSB(_npcPoderAtaque,  v => { if (_currentNpc != null) _currentNpc.PoderAtaque  = (int)v; });
            WireSB(_npcPoderEvasion, v => { if (_currentNpc != null) _currentNpc.PoderEvasion = (int)v; });

            WireSB(_npcGiveEXP,    v => { if (_currentNpc != null) _currentNpc.GiveEXP    = (int)v; });
            WireSB(_npcGiveGLD,    v => { if (_currentNpc != null) _currentNpc.GiveGLD    = (int)v; });
            WireSB(_npcGiveGLDMin, v => { if (_currentNpc != null) _currentNpc.GiveGLDMin = (int)v; });
            WireSB(_npcGiveGLDMax, v => { if (_currentNpc != null) _currentNpc.GiveGLDMax = (int)v; });
            WireSB(_npcInflacion,  v => { if (_currentNpc != null) _currentNpc.Inflacion  = (int)v; });

            WireSB(_npcSND1, v => { if (_currentNpc != null) _currentNpc.SND1 = (int)v; });
            WireSB(_npcSND2, v => { if (_currentNpc != null) _currentNpc.SND2 = (int)v; });
            WireSB(_npcSND3, v => { if (_currentNpc != null) _currentNpc.SND3 = (int)v; });
        }

        private void WireObjEvents()
        {
            WireLE(_objName,  () => { if (_currentObj != null) _currentObj.Name    = _objName!.Text; });
            WireOB(_objTypeOb,() => { if (_currentObj != null) _currentObj.ObjType = _objTypeOb!.Selected; });
            WireSB(_objGrh,   v  => { if (_currentObj != null) _currentObj.GrhIndex = (int)v; });
            WireSB(_objValor, v  => { if (_currentObj != null) _currentObj.Valor    = (int)v; });
            WireCB(_objAgarrable, v => { if (_currentObj != null) _currentObj.Agarrable = v; });

            WireSB(_objMinHIT,     v => { if (_currentObj != null) _currentObj.MinHIT     = (int)v; });
            WireSB(_objMaxHIT,     v => { if (_currentObj != null) _currentObj.MaxHIT     = (int)v; });
            WireSB(_objWeaponAnim, v => { if (_currentObj != null) _currentObj.WeaponAnim = (int)v; });
            WireCB(_objDosManos,   v => { if (_currentObj != null) _currentObj.DosManos   = v; });
            WireCB(_objProyectil,  v => { if (_currentObj != null) _currentObj.Proyectil  = v; });
            WireSB(_objMunicion,   v => { if (_currentObj != null) _currentObj.Municion   = (int)v; });

            WireSB(_objMinDef,     v => { if (_currentObj != null) _currentObj.MinDef     = (int)v; });
            WireSB(_objMaxDef,     v => { if (_currentObj != null) _currentObj.MaxDef     = (int)v; });
            WireSB(_objShieldAnim, v => { if (_currentObj != null) _currentObj.ShieldAnim = (int)v; });
            WireSB(_objCascoAnim,  v => { if (_currentObj != null) _currentObj.CascoAnim  = (int)v; });
            WireSB(_objNumRopaje,  v => { if (_currentObj != null) _currentObj.NumRopaje  = (int)v; });

            WireSB(_objTipoPocion, v => { if (_currentObj != null) _currentObj.TipoPocion     = (int)v; });
            WireSB(_objMinMod,     v => { if (_currentObj != null) _currentObj.MinModificador  = (int)v; });
            WireSB(_objMaxMod,     v => { if (_currentObj != null) _currentObj.MaxModificador  = (int)v; });
            WireSB(_objDuracion,   v => { if (_currentObj != null) _currentObj.DuracionEfecto  = (int)v; });

            WireSB(_objLlave,        v => { if (_currentObj != null) _currentObj.Llave        = (int)v; });
            WireSB(_objCerrada,      v => { if (_currentObj != null) _currentObj.Cerrada       = (int)v; });
            WireSB(_objIndexAbierta, v => { if (_currentObj != null) _currentObj.IndexAbierta  = (int)v; });
            WireSB(_objIndexCerrada, v => { if (_currentObj != null) _currentObj.IndexCerrada  = (int)v; });

            WireSB(_objSkHerr, v => { if (_currentObj != null) _currentObj.SkHerreria    = (int)v; });
            WireSB(_objSkCarp, v => { if (_currentObj != null) _currentObj.SkCarpinteria = (int)v; });
            WireSB(_objLingH,  v => { if (_currentObj != null) _currentObj.LingH         = (int)v; });
            WireSB(_objLingO,  v => { if (_currentObj != null) _currentObj.LingO         = (int)v; });
            WireSB(_objLingP,  v => { if (_currentObj != null) _currentObj.LingP         = (int)v; });
            WireSB(_objMadera, v => { if (_currentObj != null) _currentObj.Madera        = (int)v; });

            WireCB(_objReal,   v => { if (_currentObj != null) _currentObj.Real   = v; });
            WireCB(_objCaos,   v => { if (_currentObj != null) _currentObj.Caos   = v; });
            WireCB(_objNewbie, v => { if (_currentObj != null) _currentObj.Newbie = v; });
            WireSB(_objLvl,    v => { if (_currentObj != null) _currentObj.Lvl    = (int)v; });

            WireSB(_objHechIndex, v => { if (_currentObj != null) _currentObj.HechIndex = (int)v; });
            WireSB(_objSND1,      v => { if (_currentObj != null) _currentObj.SND1      = (int)v; });
        }

        private void WireSpellEvents()
        {
            WireLE(_spNombre,   () => { if (_currentSpell != null) _currentSpell.Nombre          = _spNombre!.Text; });
            WireLE(_spDesc,     () => { if (_currentSpell != null) _currentSpell.Desc            = _spDesc!.Text; });
            WireLE(_spPalabras, () => { if (_currentSpell != null) _currentSpell.PalabrasMagicas = _spPalabras!.Text; });
            WireLE(_spHechMsg,  () => { if (_currentSpell != null) _currentSpell.HechizeroMsg    = _spHechMsg!.Text; });
            WireLE(_spTargetMsg,() => { if (_currentSpell != null) _currentSpell.TargetMsg       = _spTargetMsg!.Text; });
            WireLE(_spPropioMsg,() => { if (_currentSpell != null) _currentSpell.PropioMsg       = _spPropioMsg!.Text; });

            WireOB(_spTipo,   () => { if (_currentSpell != null) _currentSpell.Tipo   = _spTipo!.Selected; });
            WireOB(_spTarget, () => { if (_currentSpell != null) _currentSpell.Target = _spTarget!.Selected; });

            WireSB(_spMinSkill, v => { if (_currentSpell != null) _currentSpell.MinSkill      = (int)v; });
            WireSB(_spMana,     v => { if (_currentSpell != null) _currentSpell.ManaRequerido = (int)v; });
            WireSB(_spSta,      v => { if (_currentSpell != null) _currentSpell.StaRequerido  = (int)v; });

            WireSB(_spSubeHP,   v => { if (_currentSpell != null) _currentSpell.SubeHP  = (int)v; });
            WireSB(_spMinHP,    v => { if (_currentSpell != null) _currentSpell.MinHP   = (int)v; });
            WireSB(_spMaxHP,    v => { if (_currentSpell != null) _currentSpell.MaxHP   = (int)v; });
            WireSB(_spSubeMana, v => { if (_currentSpell != null) _currentSpell.SubeMana = (int)v; });
            WireSB(_spMinMana,  v => { if (_currentSpell != null) _currentSpell.MinMana  = (int)v; });
            WireSB(_spMaxMana,  v => { if (_currentSpell != null) _currentSpell.MaxMana  = (int)v; });
            WireSB(_spSubeSta,  v => { if (_currentSpell != null) _currentSpell.SubeSta  = (int)v; });
            WireSB(_spMinSta,   v => { if (_currentSpell != null) _currentSpell.MinSta   = (int)v; });
            WireSB(_spMaxSta,   v => { if (_currentSpell != null) _currentSpell.MaxSta   = (int)v; });

            WireCB(_spInvis,      v => { if (_currentSpell != null) _currentSpell.Invisibilidad    = v; });
            WireCB(_spParaliza,   v => { if (_currentSpell != null) _currentSpell.Paraliza         = v; });
            WireCB(_spInmoviliza, v => { if (_currentSpell != null) _currentSpell.Inmoviliza       = v; });
            WireCB(_spEnvenena,   v => { if (_currentSpell != null) _currentSpell.Envenena         = v; });
            WireCB(_spMaldicion,  v => { if (_currentSpell != null) _currentSpell.Maldicion        = v; });
            WireCB(_spBendicion,  v => { if (_currentSpell != null) _currentSpell.Bendicion        = v; });
            WireCB(_spCuraVeneno, v => { if (_currentSpell != null) _currentSpell.CuraVeneno       = v; });
            WireCB(_spRemPar,     v => { if (_currentSpell != null) _currentSpell.RemoverParalisis = v; });
            WireCB(_spRevivir,    v => { if (_currentSpell != null) _currentSpell.Revivir          = v; });
            WireCB(_spEstupidez,  v => { if (_currentSpell != null) _currentSpell.Estupidez        = v; });
            WireCB(_spCeguera,    v => { if (_currentSpell != null) _currentSpell.Ceguera          = v; });
            WireCB(_spMimetiza,   v => { if (_currentSpell != null) _currentSpell.Mimetiza         = v; });
            WireCB(_spRemMald,    v => { if (_currentSpell != null) _currentSpell.RemoverMaldicion = v; });
            WireCB(_spRemEst,     v => { if (_currentSpell != null) _currentSpell.RemoverEstupidez = v; });

            WireSB(_spWAV,   v => { if (_currentSpell != null) _currentSpell.WAV   = (int)v; });
            WireSB(_spFXGrh, v => { if (_currentSpell != null) _currentSpell.FXGrh = (int)v; });
            WireSB(_spLoops, v => { if (_currentSpell != null) _currentSpell.Loops = (int)v; });
        }

        // Suppress flag: any of the three suppress flags blocks all handlers.
        private bool AnySuppressed => _npcSuppressEvents || _objSuppressEvents || _spSuppressEvents;

        private void WireLE(LineEdit? le, Action onChange)
        {
            if (le == null) return;
            le.TextChanged += _ => { if (!AnySuppressed) { onChange(); MarkDirty(); } };
        }

        private void WireSB(SpinBox? sb, Action<double> onChange)
        {
            if (sb == null) return;
            sb.ValueChanged += v => { if (!AnySuppressed) { onChange(v); MarkDirty(); } };
        }

        private void WireCB(CheckBox? cb, Action<bool> onChange)
        {
            if (cb == null) return;
            cb.Toggled += v => { if (!AnySuppressed) { onChange(v); MarkDirty(); } };
        }

        private void WireOB(OptionButton? ob, Action onChange)
        {
            if (ob == null) return;
            ob.ItemSelected += _ => { if (!AnySuppressed) { onChange(); MarkDirty(); } };
        }

        // ═════════════════════════════════════════════════════════════════════
        // Tab switching
        // ═════════════════════════════════════════════════════════════════════

        private void OnTabChanged(long tab)
        {
            for (int i = 0; i < _tabPanels.Length; i++)
                if (_tabPanels[i] != null)
                    _tabPanels[i]!.Visible = (i == (int)tab);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Menu
        // ═════════════════════════════════════════════════════════════════════

        private void OnMenuIdPressed(long id)
        {
            switch (id)
            {
                case 0: OnOpenFolder(); break;
                case 1: OnSaveAll();    break;
                case 2: GetTree().Quit(); break;
            }
        }

        private void OnOpenFolder()
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenDir,
                Title    = "Seleccionar carpeta dat/",
                Access   = FileDialog.AccessEnum.Filesystem,
            };
            dialog.DirSelected += path =>
            {
                dialog.QueueFree();
                LoadAllData(path);
            };
            dialog.Canceled += () => dialog.QueueFree();
            AddChild(dialog);
            dialog.PopupCentered(new Vector2I(700, 500));
        }

        private void OnSaveAll()
        {
            if (string.IsNullOrEmpty(_datDir))
            {
                SetStatus("No hay carpeta dat seleccionada.");
                return;
            }

            try
            {
                _npcDb?.Save(_datDir);
                _objDb?.Save(_datDir);
                _spellDb?.Save(_datDir);
                _expTable?.Save(_datDir);
                _craftData?.Save(_datDir);

                _dirty = false;
                UpdateDirtyIndicator();
                SetStatus($"Guardado exitosamente en {_datDir}");
            }
            catch (Exception ex)
            {
                SetStatus($"Error al guardar: {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Data loading
        // ═════════════════════════════════════════════════════════════════════

        public void LoadAllData(string datDir)
        {
            _datDir = datDir;
            SetStatus($"Cargando datos desde {datDir}…");

            try
            {
                _npcDb     = NpcDatabase.Load(datDir);
                _objDb     = ObjDatabase.Load(datDir);
                _spellDb   = SpellDatabase.Load(datDir);
                _expTable  = ExpTable.Load(datDir);
                _craftData = CraftingData.Load(datDir);

                PopulateNpcList();
                PopulateObjList();
                PopulateSpellList();
                PopulateExpFields();
                PopulateCraftLists();

                _dirty = false;
                UpdateDirtyIndicator();
                SetStatus(
                    $"Listo — {_npcDb.Npcs.Count} NPCs, {_objDb.Objects.Count} objetos, " +
                    $"{_spellDb.Spells.Count} hechizos cargados desde {datDir}");
            }
            catch (Exception ex)
            {
                SetStatus($"Error al cargar datos: {ex.Message}");
            }
        }

        // ── Populate list panels ──────────────────────────────────────────────

        private void PopulateNpcList()
        {
            if (_npcList == null || _npcDb == null) return;
            var items = new List<(int, string)>();
            foreach (var npc in _npcDb.Npcs)
                items.Add((npc.Index, $"[{npc.Index}] {npc.Name}"));
            _npcList.SetItems(items);
        }

        private void PopulateObjList()
        {
            if (_objList == null || _objDb == null) return;
            var items = new List<(int, string)>();
            foreach (var obj in _objDb.Objects)
                items.Add((obj.Index, $"[{obj.Index}] {obj.Name}"));
            _objList.SetItems(items);
        }

        private void PopulateSpellList()
        {
            if (_spellList == null || _spellDb == null) return;
            var items = new List<(int, string)>();
            foreach (var sp in _spellDb.Spells)
                items.Add((sp.Index, $"[{sp.Index}] {sp.Nombre}"));
            _spellList.SetItems(items);
        }

        private void PopulateExpFields()
        {
            if (_expTable == null) return;
            for (int lvl = 1; lvl <= ExpTable.MaxLevel; lvl++)
                if (_expFields[lvl] != null)
                    _expFields[lvl]!.Value = _expTable.Levels[lvl];
        }

        private void PopulateCraftLists()
        {
            if (_craftData == null) return;
            RefreshCraftUiList(_craftSmithWeaponList, _craftData.SmithWeapons);
            RefreshCraftUiList(_craftSmithArmorList,  _craftData.SmithArmors);
            RefreshCraftUiList(_craftCarpList,        _craftData.CarpenterItems);
        }

        private static void RefreshCraftUiList(ItemList? list, List<int> items)
        {
            if (list == null) return;
            list.Clear();
            foreach (int id in items) list.AddItem($"Obj {id}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Entity selection
        // ═════════════════════════════════════════════════════════════════════

        private void OnNpcSelected(int _)
        {
            if (_npcDb == null) return;
            int id = _npcList?.SelectedId() ?? -1;
            if (id < 0) return;
            _currentNpc = _npcDb.Npcs.Find(n => n.Index == id);
            if (_currentNpc != null) PopulateNpcFields(_currentNpc);
        }

        private void OnObjSelected(int _)
        {
            if (_objDb == null) return;
            int id = _objList?.SelectedId() ?? -1;
            if (id < 0) return;
            _currentObj = _objDb.Objects.Find(o => o.Index == id);
            if (_currentObj != null) PopulateObjFields(_currentObj);
        }

        private void OnSpellSelected(int _)
        {
            if (_spellDb == null) return;
            int id = _spellList?.SelectedId() ?? -1;
            if (id < 0) return;
            _currentSpell = _spellDb.Spells.Find(s => s.Index == id);
            if (_currentSpell != null) PopulateSpellFields(_currentSpell);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Field population (suppress events while loading)
        // ═════════════════════════════════════════════════════════════════════

        private void PopulateNpcFields(NpcData npc)
        {
            _npcSuppressEvents = true;
            try
            {
                _npcName!.Text        = npc.Name;
                _npcDesc!.Text        = npc.Desc;
                _npcTypeOb!.Selected  = Math.Clamp(npc.NpcType, 0, NpcTypeNames.Length - 1);
                _npcIndex!.Value      = npc.Index;

                _npcHead!.Value       = npc.Head;
                _npcBody!.Value       = npc.Body;
                _npcHeading!.Value    = npc.Heading;
                _npcWeaponAnim!.Value = npc.WeaponAnim;
                _npcShieldAnim!.Value = npc.ShieldAnim;
                _npcCascoAnim!.Value  = npc.CascoAnim;

                _npcMovement!.Value           = npc.Movement;
                _npcAttackable!.ButtonPressed = npc.Attackable;
                _npcHostile!.ButtonPressed    = npc.Hostile;
                _npcRespawn!.ButtonPressed    = npc.Respawn;
                _npcDomable!.Value            = npc.Domable;
                _npcComercia!.ButtonPressed   = npc.Comercia;

                _npcMinHP!.Value        = npc.MinHP;
                _npcMaxHP!.Value        = npc.MaxHP;
                _npcMinHit!.Value       = npc.MinHit;
                _npcMaxHit!.Value       = npc.MaxHit;
                _npcDef!.Value          = npc.Def;
                _npcDefM!.Value         = npc.DefM;
                _npcPoderAtaque!.Value  = npc.PoderAtaque;
                _npcPoderEvasion!.Value = npc.PoderEvasion;

                _npcGiveEXP!.Value    = npc.GiveEXP;
                _npcGiveGLD!.Value    = npc.GiveGLD;
                _npcGiveGLDMin!.Value = npc.GiveGLDMin;
                _npcGiveGLDMax!.Value = npc.GiveGLDMax;
                _npcInflacion!.Value  = npc.Inflacion;

                _npcSND1!.Value = npc.SND1;
                _npcSND2!.Value = npc.SND2;
                _npcSND3!.Value = npc.SND3;
            }
            finally { _npcSuppressEvents = false; }
        }

        private void PopulateObjFields(ObjData obj)
        {
            _objSuppressEvents = true;
            try
            {
                _objName!.Text        = obj.Name;
                _objTypeOb!.Selected   = Math.Clamp(obj.ObjType, 0, ObjTypeNames.Length - 1);
                _objGrh!.Value        = obj.GrhIndex;
                _objValor!.Value      = obj.Valor;
                _objAgarrable!.ButtonPressed = obj.Agarrable;

                _objMinHIT!.Value           = obj.MinHIT;
                _objMaxHIT!.Value           = obj.MaxHIT;
                _objWeaponAnim!.Value       = obj.WeaponAnim;
                _objDosManos!.ButtonPressed  = obj.DosManos;
                _objProyectil!.ButtonPressed = obj.Proyectil;
                _objMunicion!.Value         = obj.Municion;

                _objMinDef!.Value     = obj.MinDef;
                _objMaxDef!.Value     = obj.MaxDef;
                _objShieldAnim!.Value = obj.ShieldAnim;
                _objCascoAnim!.Value  = obj.CascoAnim;
                _objNumRopaje!.Value  = obj.NumRopaje;

                _objTipoPocion!.Value = obj.TipoPocion;
                _objMinMod!.Value     = obj.MinModificador;
                _objMaxMod!.Value     = obj.MaxModificador;
                _objDuracion!.Value   = obj.DuracionEfecto;

                _objLlave!.Value        = obj.Llave;
                _objCerrada!.Value      = obj.Cerrada;         // int field
                _objIndexAbierta!.Value = obj.IndexAbierta;
                _objIndexCerrada!.Value = obj.IndexCerrada;

                _objSkHerr!.Value = obj.SkHerreria;
                _objSkCarp!.Value = obj.SkCarpinteria;
                _objLingH!.Value  = obj.LingH;
                _objLingO!.Value  = obj.LingO;
                _objLingP!.Value  = obj.LingP;
                _objMadera!.Value = obj.Madera;

                _objReal!.ButtonPressed   = obj.Real;
                _objCaos!.ButtonPressed   = obj.Caos;
                _objNewbie!.ButtonPressed = obj.Newbie;
                _objLvl!.Value            = obj.Lvl;

                _objHechIndex!.Value = obj.HechIndex;
                _objSND1!.Value      = obj.SND1;
            }
            finally { _objSuppressEvents = false; }
        }

        private void PopulateSpellFields(SpellData sp)
        {
            _spSuppressEvents = true;
            try
            {
                _spNombre!.Text   = sp.Nombre;
                _spDesc!.Text     = sp.Desc;
                _spPalabras!.Text = sp.PalabrasMagicas;

                _spHechMsg!.Text   = sp.HechizeroMsg;
                _spTargetMsg!.Text = sp.TargetMsg;
                _spPropioMsg!.Text = sp.PropioMsg;

                _spTipo!.Selected   = Math.Clamp(sp.Tipo,   0, SpellTypeNames.Length   - 1);
                _spTarget!.Selected = Math.Clamp(sp.Target, 0, SpellTargetNames.Length - 1);

                _spMinSkill!.Value = sp.MinSkill;
                _spMana!.Value     = sp.ManaRequerido;
                _spSta!.Value      = sp.StaRequerido;

                _spSubeHP!.Value   = sp.SubeHP;
                _spMinHP!.Value    = sp.MinHP;
                _spMaxHP!.Value    = sp.MaxHP;
                _spSubeMana!.Value = sp.SubeMana;
                _spMinMana!.Value  = sp.MinMana;
                _spMaxMana!.Value  = sp.MaxMana;
                _spSubeSta!.Value  = sp.SubeSta;
                _spMinSta!.Value   = sp.MinSta;
                _spMaxSta!.Value   = sp.MaxSta;

                _spInvis!.ButtonPressed      = sp.Invisibilidad;
                _spParaliza!.ButtonPressed   = sp.Paraliza;
                _spInmoviliza!.ButtonPressed = sp.Inmoviliza;
                _spEnvenena!.ButtonPressed   = sp.Envenena;
                _spMaldicion!.ButtonPressed  = sp.Maldicion;
                _spBendicion!.ButtonPressed  = sp.Bendicion;
                _spCuraVeneno!.ButtonPressed = sp.CuraVeneno;
                _spRemPar!.ButtonPressed     = sp.RemoverParalisis;
                _spRevivir!.ButtonPressed    = sp.Revivir;
                _spEstupidez!.ButtonPressed  = sp.Estupidez;
                _spCeguera!.ButtonPressed    = sp.Ceguera;
                _spMimetiza!.ButtonPressed   = sp.Mimetiza;
                _spRemMald!.ButtonPressed    = sp.RemoverMaldicion;
                _spRemEst!.ButtonPressed     = sp.RemoverEstupidez;

                _spWAV!.Value   = sp.WAV;
                _spFXGrh!.Value = sp.FXGrh;
                _spLoops!.Value = sp.Loops;
            }
            finally { _spSuppressEvents = false; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Add new entity
        // ═════════════════════════════════════════════════════════════════════

        private void OnNpcAdd()
        {
            if (_npcDb == null) return;
            int nextId = _npcDb.Npcs.Count == 0 ? 1 : _npcDb.Npcs[^1].Index + 1;
            var n = new NpcData { Index = nextId, Name = $"Nuevo NPC {nextId}" };
            _npcDb.Npcs.Add(n);
            PopulateNpcList();
            _npcList?.SelectById(nextId);
            MarkDirty();
        }

        private void OnObjAdd()
        {
            if (_objDb == null) return;
            int nextId = _objDb.Objects.Count == 0 ? 1 : _objDb.Objects[^1].Index + 1;
            var o = new ObjData { Index = nextId, Name = $"Nuevo Objeto {nextId}" };
            _objDb.Objects.Add(o);
            PopulateObjList();
            _objList?.SelectById(nextId);
            MarkDirty();
        }

        private void OnSpellAdd()
        {
            if (_spellDb == null) return;
            int nextId = _spellDb.Spells.Count == 0 ? 1 : _spellDb.Spells[^1].Index + 1;
            var s = new SpellData { Index = nextId, Nombre = $"Nuevo Hechizo {nextId}" };
            _spellDb.Spells.Add(s);
            PopulateSpellList();
            _spellList?.SelectById(nextId);
            MarkDirty();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Crafting CRUD
        // ═════════════════════════════════════════════════════════════════════

        private enum CraftListKind { SmithWeapon, SmithArmor, Carpenter }

        private void OnCraftAdd(CraftListKind kind)
        {
            if (_craftData == null) return;
            var dataList = GetCraftDataList(kind);
            var uiList   = GetCraftUiList(kind);
            dataList.Add(0);
            uiList?.AddItem("Obj 0");
            MarkDirty();
        }

        private void OnCraftRemove(CraftListKind kind, ItemList? uiList)
        {
            if (_craftData == null || uiList == null) return;
            var sel = uiList.GetSelectedItems();
            if (sel.Length == 0) return;
            int idx = sel[0];
            var dataList = GetCraftDataList(kind);
            if (idx >= 0 && idx < dataList.Count)
            {
                dataList.RemoveAt(idx);
                uiList.RemoveItem(idx);
                MarkDirty();
            }
        }

        private List<int> GetCraftDataList(CraftListKind kind) => kind switch
        {
            CraftListKind.SmithWeapon => _craftData!.SmithWeapons,
            CraftListKind.SmithArmor  => _craftData!.SmithArmors,
            _                         => _craftData!.CarpenterItems,
        };

        private ItemList? GetCraftUiList(CraftListKind kind) => kind switch
        {
            CraftListKind.SmithWeapon => _craftSmithWeaponList,
            CraftListKind.SmithArmor  => _craftSmithArmorList,
            _                         => _craftCarpList,
        };

        // ═════════════════════════════════════════════════════════════════════
        // Dirty / status
        // ═════════════════════════════════════════════════════════════════════

        private void MarkDirty()
        {
            _dirty = true;
            UpdateDirtyIndicator();
        }

        private void UpdateDirtyIndicator()
        {
            if (_dirtyLabel != null)
                _dirtyLabel.Text = _dirty ? "● Cambios sin guardar" : string.Empty;
        }

        public void SetStatus(string msg)
        {
            if (_statusLabel != null)
                _statusLabel.Text = msg;
        }
    }
}
