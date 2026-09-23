using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VNdev.Core;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Vẽ panel thuộc tính bên phải theo loại node đang chọn. Chỉ những trường
/// mà việc kéo dây trong <see cref="GraphSync"/> không lo được (nội dung,
/// không phải liên kết) mới nằm ở đây.
/// </summary>
public static class NodeInspector
{
    public static void Populate(VBoxContainer container, ProjectData project, StoryNode node, Action onChanged, Action<string>? onOpenScene = null)
    {
        foreach (var child in container.GetChildren()) child.QueueFree();

        var header = new Label { Text = $"{GraphSync.TypeIcon(node)}  {TypeName(node)}" };
        header.AddThemeColorOverride("font_color", Palette.Text);
        header.AddThemeFontSizeOverride("font_size", 15);
        container.AddChild(header);

        var idLabel = new Label { Text = $"id: {node.Id}" };
        idLabel.AddThemeColorOverride("font_color", Palette.Text3);
        container.AddChild(idLabel);

        AddField(container, "Nhãn (chỉ hiện trên node)", node.Label ?? "", text =>
        {
            node.Label = string.IsNullOrEmpty(text) ? null : text;
            onChanged();
        });

        container.AddChild(new HSeparator());

        switch (node)
        {
            case SceneNode scene:
                AddField(container, "Id cảnh", scene.Scene, text => { scene.Scene = text; onChanged(); });
                if (onOpenScene is not null && !string.IsNullOrEmpty(scene.Scene))
                {
                    var open = new Button { Text = "→ Mở trong Scene Canvas" };
                    open.Pressed += () => onOpenScene(scene.Scene);
                    container.AddChild(open);
                }
                break;

            case ChoiceNode choice:
                PopulateChoice(container, project, choice, onChanged);
                break;

            case ConditionNode condition:
                PopulateCondition(container, project, condition, onChanged);
                break;

            case VariableNode variable:
                PopulateVariable(container, project, variable, onChanged);
                break;

            case EndingNode ending:
                var locale = project.PrimaryLocale;
                AddField(container, "Tên kết thúc", ending.Name[locale] ?? "", text =>
                {
                    ending.Name[locale] = text;
                    onChanged();
                });
                break;

            case CommentNode comment:
                var edit = new TextEdit { Text = comment.Text, CustomMinimumSize = new Vector2(0, 120) };
                edit.TextChanged += () => { comment.Text = edit.Text; onChanged(); };
                container.AddChild(edit);
                break;

            case JumpNode:
                var hint = new Label
                {
                    Text = "Kéo dây từ cổng ra của node này tới đích để đặt điểm nhảy.",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                hint.AddThemeColorOverride("font_color", Palette.Text3);
                container.AddChild(hint);
                break;
        }
    }

    private static void PopulateChoice(VBoxContainer container, ProjectData project, ChoiceNode choice, Action onChanged)
    {
        var locale = "vi";
        AddField(container, "Câu hỏi hiển thị", choice.Prompt?[locale] ?? "", text =>
        {
            choice.Prompt ??= new Localized();
            choice.Prompt[locale] = text;
            onChanged();
        });

        var listLabel = new Label { Text = $"Các lựa chọn ({choice.Options.Count})" };
        listLabel.AddThemeColorOverride("font_color", Palette.Text3);
        container.AddChild(listLabel);

        foreach (var option in choice.Options.ToList())
        {
            var optionBox = new VBoxContainer();
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 });
            panel.AddChild(optionBox);

            var row = new HBoxContainer();
            var text = new LineEdit { Text = option.Text[locale] ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.TextChanged += t =>
            {
                option.Text[locale] = t;
                onChanged();
            };
            var remove = new Button { Text = "x" };
            remove.Pressed += () =>
            {
                choice.Options.Remove(option);
                onChanged();
            };
            row.AddChild(text);
            row.AddChild(remove);
            optionBox.AddChild(row);

            PopulateEffects(optionBox, project, option.Effects, onChanged);

            container.AddChild(panel);
        }

        var add = new Button { Text = "+ Thêm lựa chọn" };
        add.Pressed += () =>
        {
            var id = Ids.Make("opt", $"lua chon {choice.Options.Count + 1}", choice.Options.Select(o => o.Id));
            choice.Options.Add(new ChoiceOption { Id = id, Text = new Localized(locale, "Lựa chọn mới") });
            onChanged();
        };
        container.AddChild(add);
    }

    /// <summary>
    /// Danh sách tác động dùng chung cho cả lựa chọn (Choice) và node Biến —
    /// đúng nguyên tắc "trình dựng bằng dropdown" ở Chặng 3, không bắt người
    /// viết truyện gõ tên biến bằng tay (dễ gõ sai, bộ kiểm tra sẽ báo lỗi).
    /// </summary>
    private static void PopulateEffects(VBoxContainer container, ProjectData project, List<Effect> effects, Action onChanged)
    {
        foreach (var effect in effects.ToList())
        {
            var row = new HBoxContainer();

            var varBtn = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var v in project.Variables) varBtn.AddItem(v.Id);
            var idx = project.Variables.FindIndex(v => v.Id == effect.Variable);
            if (idx >= 0) varBtn.Selected = idx;
            varBtn.ItemSelected += i =>
            {
                effect.Variable = project.Variables[(int)i].Id;
                onChanged();
            };

            var opBtn = new OptionButton();
            foreach (var op in Enum.GetValues<AssignOp>()) opBtn.AddItem(op.ToString());
            opBtn.Selected = (int)effect.Op;
            opBtn.ItemSelected += i =>
            {
                effect.Op = (AssignOp)i;
                onChanged();
            };

            var remove = new Button { Text = "x" };
            remove.Pressed += () =>
            {
                effects.Remove(effect);
                onChanged();
            };

            row.AddChild(varBtn);
            row.AddChild(opBtn);
            row.AddChild(remove);
            container.AddChild(row);
        }

        if (project.Variables.Count == 0)
        {
            var hint = new Label { Text = "Chưa có biến — khai báo ở tab Nhân vật trước." };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            container.AddChild(hint);
            return;
        }

        var add = new Button { Text = "+ Thêm tác động" };
        add.Pressed += () =>
        {
            effects.Add(new Effect { Variable = project.Variables[0].Id, Op = AssignOp.Add, Value = VarValue.Of(1d) });
            onChanged();
        };
        container.AddChild(add);
    }

    private static void PopulateCondition(VBoxContainer container, ProjectData project, ConditionNode condition, Action onChanged)
    {
        if (condition.Condition is not CompareCondition cmp)
        {
            var note = new Label { Text = "Điều kiện ghép (and/or) — trình dựng nhiều tầng làm ở chặng sau, đây chỉ hỗ trợ so sánh đơn." };
            note.AddThemeColorOverride("font_color", Palette.Text3);
            container.AddChild(note);
            return;
        }

        var varBtn = new OptionButton();
        foreach (var v in project.Variables) varBtn.AddItem(v.Id);
        var currentIdx = project.Variables.FindIndex(v => v.Id == cmp.Variable);
        if (currentIdx >= 0) varBtn.Selected = currentIdx;
        varBtn.ItemSelected += idx =>
        {
            cmp.Variable = project.Variables[(int)idx].Id;
            onChanged();
        };
        AddLabeled(container, "Biến", varBtn);

        var opBtn = new OptionButton();
        foreach (var op in Enum.GetValues<ComparisonOp>()) opBtn.AddItem(op.ToString());
        opBtn.Selected = (int)cmp.Op;
        opBtn.ItemSelected += idx =>
        {
            cmp.Op = (ComparisonOp)idx;
            onChanged();
        };
        AddLabeled(container, "So sánh", opBtn);

        AddField(container, "Giá trị", cmp.Value.ToString(), text =>
        {
            cmp.Value = double.TryParse(text, out var num) ? VarValue.Of(num) : VarValue.Of(text);
            onChanged();
        });
    }

    private static void PopulateVariable(VBoxContainer container, ProjectData project, VariableNode variable, Action onChanged)
    {
        var listLabel = new Label { Text = $"Tác động ({variable.Effects.Count})" };
        listLabel.AddThemeColorOverride("font_color", Palette.Text3);
        container.AddChild(listLabel);

        PopulateEffects(container, project, variable.Effects, onChanged);
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

    private static void AddLabeled(VBoxContainer container, string label, Control control)
    {
        var lbl = new Label { Text = label };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        container.AddChild(lbl);
        container.AddChild(control);
    }

    private static string TypeName(StoryNode node) => node switch
    {
        SceneNode => "Cảnh",
        ChoiceNode => "Lựa chọn",
        ConditionNode => "Điều kiện",
        VariableNode => "Biến",
        JumpNode => "Nhảy",
        EndingNode => "Kết thúc",
        CommentNode => "Ghi chú",
        _ => "?",
    };
}
