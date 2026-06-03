using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Imbalances.Client.Models;

public sealed class FileTreeItem : ITreeItemData<FileTreeItem>
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public int Level { get; set; }
    public IBrowserFile? BrowserFile { get; set; }

    private bool? _isSelected;
    public bool? IsSelected
    {
        get
        {
            if (!IsFolder) return _isSelected;
            if (!Children.Any()) return _isSelected;

            var allSelected = Children.All(c => c.IsSelected == true);
            var noneSelected = Children.All(c => c.IsSelected == false);

            if (allSelected) return true;
            if (noneSelected) return false;
            return null;
        }
        set
        {
            _isSelected = value ?? false;
            if (!IsFolder) return;
            foreach (var child in Children)
            {
                child.IsSelected = value;
            }
        }
    }

    public bool IsExpanded { get; set; } = true;
    public List<FileTreeItem> Children { get; set; } = new();
    public bool Visible { get; set; } = true;

    public bool HasChildren => Children.Count > 0;
    public string IndentClass => Level <= 30 ? $"tree-indent-{Level}" : "tree-indent-30";

    public string? Text { get => Name; set => Name = value ?? string.Empty; }
    public string? Icon { get; set; }
    public string? IconColor { get; set; }
    public string? SelectedIcon { get; set; }
    public bool Expanded { get => IsExpanded; set => IsExpanded = value; }

    public FileTreeItem Value => this;

    public bool Expandable
    {
        get => HasChildren;
        set { }
    }

    public bool Selected
    {
        get => IsSelected == true;
        set => IsSelected = value;
    }

    IReadOnlyCollection<ITreeItemData<FileTreeItem>>? ITreeItemData<FileTreeItem>.Children
    {
        get => new ChildrenViewCollection(Children);
        set
        {
            var next = new List<FileTreeItem>();
            if (value != null)
            {
                foreach (var item in value)
                {
                    if (item?.Value is FileTreeItem child)
                    {
                        next.Add(child);
                    }
                }
            }
            Children = next;
        }
    }

    private sealed class ChildrenViewCollection : IReadOnlyCollection<ITreeItemData<FileTreeItem>>
    {
        private readonly List<FileTreeItem> _children;

        public ChildrenViewCollection(List<FileTreeItem> children)
        {
            _children = children;
        }

        public int Count => _children.Count;

        public IEnumerator<ITreeItemData<FileTreeItem>> GetEnumerator()
        {
            foreach (var child in _children)
            {
                yield return child;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
