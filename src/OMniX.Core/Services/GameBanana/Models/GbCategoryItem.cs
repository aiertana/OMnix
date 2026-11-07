namespace OMniX.Core.Services.GameBanana.Models;

/// <summary>商店分类下拉项：解析好编号的分类（根分类或角色子分类）。</summary>
public sealed record GbCategoryItem(int Id, string Name, int? Count = null, string? IconUrl = null);
