using System;
using System.Diagnostics;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <example>
    /// <code>
    /// [URPHelpURL("urp/Volumes")]
    /// public class VolumeProfile : ScriptableObject { /* ... */ }
    /// </code>
    /// </example>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum, AllowMultiple = false)]
    internal class URPHelpURLAttribute : HelpURLAttribute
    {
        /// <param name="pageName">The page path relative to the Manual root, without the <c>.html</c> extension (for example <c>"urp/Volumes"</c>).</param>
        /// <param name="pageHash">Optional section anchor on the page, with or without the leading <c>#</c>.</param>
        public URPHelpURLAttribute(string pageName, string pageHash = "")
            : base(DocumentationInfo.GetManualLink(pageName, pageHash))
        {
        }
    }
}
