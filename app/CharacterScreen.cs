using System;
using System.Linq;
using Godot;
using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Màn hình Chặng 3: quản lý nhân vật (tên, màu, biểu cảm, hồ sơ giọng) và
/// panel biến của dự án. Gộp chung một màn hình vì cả hai đều là "khai báo
/// trước khi soạn cảnh mới có gì để chọn" — xem docs/LO-TRINH.md.
/// </summary>
public partial class CharacterScreen : Control
{
    private readonly LoadedProject _loaded;
    private readonly IFileSystem _fs;
    private readonly Action _onChanged;

    private ItemList _charList = null!;
    private VBoxContainer _detailBox = null!;
    private VBoxContainer _variablesBox = null!;
    private string? _selectedCharacterId;

    public CharacterScreen(LoadedProject loaded, IFileSystem fs, Action onChanged)
    {
        _loaded = loaded;
        _fs = fs;
        _onChanged = onChanged;
    }

    public override void _Ready()
    {
        var body = new HBoxContainer();
        body.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(body);

        body.AddChild(BuildCharacterListPanel());
        body.AddChild(BuildDetailPanel());
        body.AddChild(BuildVariablesPanel());

        RefreshCharacterList();
        RefreshVariablesList();
    }

    private Control BuildCharacterListPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(180, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel });

        var box = new VBoxContainer();
        panel.AddChild(box);

        box.AddChild(Header("Nhân vật"));
        _charList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _charList.ItemSelected += idx => SelectCharacter(_loaded.Project.Characters[(int)idx]);
        box.AddChild(_charList);

        var add = new Button { Text = "+ Thêm nhân vật" };
        add.Pressed += AddCharacter;
        box.AddChild(add);

        return panel;
    }

    private Control BuildDetailPanel()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);

        _detailBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detailBox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_detailBox);

        ShowNoSelection();
        return panel;
    }

    private Control BuildVariablesPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel });

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);

        var outer = new VBoxContainer();
        scroll.AddChild(outer);

        outer.AddChild(Header("Biến"));
        _variablesBox = new VBoxContainer();
        _variablesBox.AddThemeConstantOverride("separation", 6);
        outer.AddChild(_variablesBox);

        var add = new Button { Text = "+ Khai báo biến" };
        add.Pressed += AddVariable;
        outer.AddChild(add);

        return panel;
    }

    private static Label Header(string text)
    {
        var lbl = new Label { Text = text.ToUpperInvariant() };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        return lbl;
    }

    // ---------- Nhân vật ----------

    private void RefreshCharacterList()
    {
        _charList.Clear();
        foreach (var id in _loaded.Project.Characters)
        {
            var c = _loaded.Characters[id];
            _charList.AddItem(c.DisplayName[_loaded.Project.PrimaryLocale] ?? id);
        }

        if (_selectedCharacterId is not null)
        {
            var idx = _loaded.Project.Characters.IndexOf(_selectedCharacterId);
            if (idx >= 0) _charList.Select(idx);
        }
    }

    private void AddCharacter()
    {
        var id = Ids.Make("char", $"nhan vat {_loaded.Project.Characters.Count + 1}", _loaded.Project.Characters);
        var character = new CharacterData
        {
            Id = id,
            DisplayName = new Localized(_loaded.Project.PrimaryLocale, "Nhân vật mới"),
            Expressions = { new VNdev.Core.Model.Expression { Id = "normal" } },
        };

        _loaded.Project.Characters.Add(id);
        _loaded.Characters[id] = character;
        ProjectIo.SaveProject(_fs, _loaded.Project);
        ProjectIo.SaveCharacter(_fs, character);

        RefreshCharacterList();
        SelectCharacter(id);
        _onChanged();
    }

    private void SelectCharacter(string id)
    {
        _selectedCharacterId = id;
        var character = _loaded.Characters[id];

        foreach (var child in _detailBox.GetChildren()) child.QueueFree();

        var locale = _loaded.Project.PrimaryLocale;

        AddField(_detailBox, "Tên hiển thị", character.DisplayName[locale] ?? "", text =>
        {
            character.DisplayName[locale] = text;
            SaveCharacter(character);
            RefreshCharacterList();
        });

        AddField(_detailBox, "Màu tên (#RRGGBB)", character.Color, text =>
        {
            character.Color = text;
            SaveCharacter(character);
        });

        AddField(_detailBox, "Hồ sơ giọng (AI đọc trước khi viết thoại)", character.VoiceProfile ?? "", text =>
        {
            character.VoiceProfile = string.IsNullOrEmpty(text) ? null : text;
            SaveCharacter(character);
        });

        _detailBox.AddChild(new HSeparator());

        var exprHeader = new Label { Text = $"Biểu cảm ({character.Expressions.Count})" };
        exprHeader.AddThemeColorOverride("font_color", Palette.Text3);
        _detailBox.AddChild(exprHeader);

        var sprites = AssetIndex.Sprites(_fs);

        foreach (var expr in character.Expressions.ToList())
        {
            var row = new HBoxContainer();
            var idEdit = new LineEdit { Text = expr.Id, SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "id biểu cảm" };
            idEdit.TextChanged += text => { expr.Id = text; SaveCharacter(character); };

            // Chọn từ ảnh đã quét được ở tab Asset thay vì gõ tay đường dẫn —
            // gõ tay dễ sai và không thấy trước ảnh nào đang gán.
            var spritePick = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            spritePick.AddItem("— chưa chọn ảnh —");
            var selected = 0;
            for (var i = 0; i < sprites.Count; i++)
            {
                spritePick.AddItem(System.IO.Path.GetFileName(sprites[i]));
                if (sprites[i] == expr.Sprite) selected = i + 1;
            }
            spritePick.Selected = selected;
            spritePick.ItemSelected += idx =>
            {
                expr.Sprite = idx == 0 ? null : sprites[(int)idx - 1];
                SaveCharacter(character);
            };

            var remove = new Button { Text = "x" };
            remove.Pressed += () =>
            {
                character.Expressions.Remove(expr);
                SaveCharacter(character);
                SelectCharacter(id);
            };
            row.AddChild(idEdit);
            row.AddChild(spritePick);
            row.AddChild(remove);
            _detailBox.AddChild(row);
        }

        if (sprites.Count == 0)
        {
            var hint = new Label { Text = "Chưa có sprite nào — chép ảnh vào assets/sprites rồi xem ở tab Asset." };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detailBox.AddChild(hint);
        }

        var addExpr = new Button { Text = "+ Thêm biểu cảm" };
        addExpr.Pressed += () =>
        {
            var exprId = Ids.Make("expr", $"bieu cam {character.Expressions.Count + 1}", character.Expressions.Select(e => e.Id));
            character.Expressions.Add(new VNdev.Core.Model.Expression { Id = exprId });
            SaveCharacter(character);
            SelectCharacter(id);
        };
        _detailBox.AddChild(addExpr);
    }

    private void ShowNoSelection()
    {
        var hint = new Label { Text = "Chọn hoặc thêm một nhân vật.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeColorOverride("font_color", Palette.Text3);
        _detailBox.AddChild(hint);
    }

    private void SaveCharacter(CharacterData character)
    {
        ProjectIo.SaveCharacter(_fs, character);
        _onChanged();
    }

    // ---------- Biến ----------

    private void RefreshVariablesList()
    {
        foreach (var child in _variablesBox.GetChildren()) child.QueueFree();

        foreach (var v in _loaded.Project.Variables.ToList())
        {
            _variablesBox.AddChild(BuildVariableRow(v));
        }
    }

    private Control BuildVariableRow(VariableDef v)
    {
        var box = new VBoxContainer();
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel3, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 });
        panel.AddChild(box);

        var idRow = new HBoxContainer();
        var idEdit = new LineEdit { Text = v.Id, SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "tên biến" };
        idEdit.TextChanged += text => { v.Id = text; SaveVariables(); };
        var remove = new Button { Text = "x" };
        remove.Pressed += () =>
        {
            _loaded.Project.Variables.Remove(v);
            SaveVariables();
            RefreshVariablesList();
        };
        idRow.AddChild(idEdit);
        idRow.AddChild(remove);
        box.AddChild(idRow);

        var typeBtn = new OptionButton();
        foreach (var t in Enum.GetValues<VariableType>()) typeBtn.AddItem(TypeLabel(t));
        typeBtn.Selected = (int)v.Type;
        typeBtn.ItemSelected += idx =>
        {
            v.Type = (VariableType)idx;
            v.Initial = VarValue.Default(v.Type);
            SaveVariables();
            RefreshVariablesList();
        };
        box.AddChild(typeBtn);

        var initialEdit = new LineEdit { Text = v.Initial.ToString(), PlaceholderText = "giá trị ban đầu" };
        initialEdit.TextChanged += text => { v.Initial = ParseInitial(v.Type, text); SaveVariables(); };
        box.AddChild(initialEdit);

        var persistRow = new HBoxContainer();
        var persistLabel = new Label { Text = "Giữ qua nhiều lần chơi" };
        persistLabel.AddThemeColorOverride("font_color", Palette.Text2);
        var persistCheck = new CheckBox { ButtonPressed = v.Persistent };
        persistCheck.Toggled += pressed => { v.Persistent = pressed; SaveVariables(); };
        persistRow.AddChild(persistLabel);
        persistRow.AddChild(persistCheck);
        box.AddChild(persistRow);

        return panel;
    }

    private static VarValue ParseInitial(VariableType type, string text) => type switch
    {
        VariableType.Number => VarValue.Of(double.TryParse(text, out var n) ? n : 0),
        VariableType.Boolean => VarValue.Of(text.Trim().ToLowerInvariant() is "true" or "1" or "đúng"),
        _ => VarValue.Of(text),
    };

    private static string TypeLabel(VariableType t) => t switch
    {
        VariableType.Number => "Số",
        VariableType.Boolean => "Đúng/sai",
        VariableType.Text => "Chữ",
        _ => "?",
    };

    private void AddVariable()
    {
        var id = Ids.Make("var", $"bien {_loaded.Project.Variables.Count + 1}", _loaded.Project.Variables.Select(v => v.Id));
        _loaded.Project.Variables.Add(new VariableDef { Id = id, Type = VariableType.Number, Initial = VarValue.Of(0d) });
        SaveVariables();
        RefreshVariablesList();
    }

    private void SaveVariables()
    {
        ProjectIo.SaveProject(_fs, _loaded.Project);
        _onChanged();
    }

    private static void AddField(VBoxContainer container, string label, string value, Action<string> onChanged)
    {
        var lbl = new Label { Text = label };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        container.AddChild(lbl);

        var edit = new LineEdit { Text = value };
        edit.TextChanged += text => onChanged(text);
        container.AddChild(edit);
    }
}
