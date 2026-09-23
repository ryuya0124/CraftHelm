using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace CraftHarbor.Desktop;

// AvalonEdit does not expose a line-height setting. A tiny, non-interactive
// inline element lets WPF's text formatter reserve the same height for every
// visible line, so caret, selection and line numbers use the real layout.
internal sealed class EditorLineSpacing(TextEditor editor) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var end = CurrentContext.VisualLine.FirstDocumentLine.EndOffset;
        return startOffset <= end ? end : -1;
    }

    public override VisualLineElement ConstructElement(int offset) =>
        new InlineObjectElement(0, new Border { Width = 0.1, Height = editor.FontSize * 1.5, IsHitTestVisible = false });
}
