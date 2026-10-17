using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.Core.Tests;

public class SaveGameTests
{
    private static LoadedProject Project()
    {
        var loaded = ProjectIo.CreateNew("Thử lưu", null, new List<string> { "vi" });
        var graph = loaded.Graphs["chapter_1"];
        graph.Nodes.Add(new SceneNode { Id = "scene_mo_dau", Scene = "mo_dau", Next = "choice_di_dau" });
        graph.Nodes.Add(new ChoiceNode
        {
            Id = "choice_di_dau",
            Options = { new ChoiceOption { Id = "o1", Text = new Localized("vi", "Đi"), Next = "ending_ket_thuc" } },
        });
        graph.Entry = "scene_mo_dau";
        loaded.Scenes["mo_dau"] = new SceneData
        {
            Id = "mo_dau",
            Lines =
            {
                new DialogueLine { Id = "l1", Text = new Localized("vi", "Một") },
                new DialogueLine { Id = "l2", Text = new Localized("vi", "Hai") },
                new DialogueLine { Id = "l3", Text = new Localized("vi", "Ba") },
            },
        };
        loaded.Project.Variables.Add(new VariableDef { Id = "thien_cam", Type = VariableType.Number, Initial = VarValue.Of(0d) });
        return loaded;
    }

    private static SaveGame At(string node, int line, string? scene = null) => new()
    {
        SavedAt = new DateTimeOffset(2026, 10, 17, 9, 30, 0, TimeSpan.FromHours(7)),
        Graph = "chapter_1",
        Node = node,
        Scene = scene ?? node,
        Line = line,
        Variables = { ["thien_cam"] = VarValue.Of(3d) },
        Preview = "Hai",
    };

    [Test]
    public void Ghi_roi_doc_lai_o_luu_giu_nguyen_vi_tri_va_bien()
    {
        var store = new SaveStore(new MemoryFileSystem());
        store.Write("1", At("scene_mo_dau", 1));

        var back = store.Read("1");
        Check.NotNull(back);
        Check.Equal("scene_mo_dau", back.Node);
        Check.Equal(1, back.Line);
        Check.Equal(VarValue.Of(3d), back.Variables["thien_cam"]);
        Check.Equal(At("x", 0).SavedAt, back.SavedAt);
    }

    [Test]
    public void O_trong_va_o_hong_deu_tra_null_khong_nem_loi()
    {
        var fs = new MemoryFileSystem();
        var store = new SaveStore(fs);
        fs.WriteText("2.save.json", "{ bị cắt dở");

        Check.Null(store.Read("1"));
        Check.Null(store.Read("2"));
    }

    [Test]
    public void Ten_o_luu_la_bi_chan_de_khong_thanh_duong_dan()
    {
        var store = new SaveStore(new MemoryFileSystem());
        Check.Throws<ArgumentException>(() => store.Write("../ngoai", At("scene_mo_dau", 0)));
    }

    [Test]
    public void Tai_ban_luu_giua_canh_chay_tiep_dung_dong()
    {
        var r = At("scene_mo_dau", 2).Resolve(Project());
        Check.True(r.Ok, r.Error);
        Check.Equal("scene_mo_dau", r.Node!.Id);
        Check.Equal(2, r.Line);
        Check.Equal(VarValue.Of(3d), r.Variables["thien_cam"]);
    }

    [Test]
    public void Canh_bi_xoa_bot_dong_thi_lui_ve_dong_cuoi_thay_vi_bao_loi()
    {
        var r = At("scene_mo_dau", 9).Resolve(Project());
        Check.True(r.Ok, r.Error);
        Check.Equal(2, r.Line);
    }

    [Test]
    public void Luu_luc_dang_chon_giu_canh_nen_phia_sau()
    {
        var r = At("choice_di_dau", 2, scene: "scene_mo_dau").Resolve(Project());
        Check.True(r.Ok, r.Error);
        Check.True(r.Node is ChoiceNode);
        Check.Equal("scene_mo_dau", r.Backdrop!.Id);
    }

    [Test]
    public void Node_luu_da_bi_xoa_thi_bao_loi_ro_rang()
    {
        var r = At("scene_da_xoa", 0).Resolve(Project());
        Check.False(r.Ok);
        Check.True(r.Error!.Contains("scene_da_xoa"));
    }

    [Test]
    public void Bien_moi_khai_bao_lay_gia_tri_dau_bien_da_xoa_bi_bo()
    {
        var loaded = Project();
        loaded.Project.Variables.Add(new VariableDef { Id = "da_gap", Type = VariableType.Boolean, Initial = VarValue.Of(true) });
        var save = At("scene_mo_dau", 0);
        save.Variables["bien_cu"] = VarValue.Of(1d);

        var r = save.Resolve(loaded);
        Check.Equal(VarValue.Of(true), r.Variables["da_gap"]);
        Check.False(r.Variables.ContainsKey("bien_cu"));
    }
}
