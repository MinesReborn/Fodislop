using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kern.ArchitectureLinter.Scanning;

/// <summary>Ссылки на поля, перечисляемые через typeof(T).GetFields.</summary>
internal static class CSharpReflectionReferences
{
    public static HashSet<(string File, string Name)> CollectFields(IEnumerable<string> files)
    {
        var roots = files.Select(file => (File: file, Root: CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot())).ToArray();
        var queries = new List<(string Type, bool Public, bool NonPublic, bool Static, bool Instance)>();
        foreach (var (_, root) in roots)
        {
            foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is not MemberAccessExpressionSyntax
                    {
                        Expression: TypeOfExpressionSyntax typeOf,
                        Name.Identifier.ValueText: "GetFields",
                    })
                {
                    continue;
                }

                var flags = call.ArgumentList.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Where(member => member.Expression.ToString().EndsWith("BindingFlags", StringComparison.Ordinal))
                    .Select(member => member.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
                bool defaults = call.ArgumentList.Arguments.Count == 0;
                queries.Add((typeOf.Type.ToString(), defaults || flags.Contains("Public"),
                    flags.Contains("NonPublic"), defaults || flags.Contains("Static"), defaults || flags.Contains("Instance")));
            }
        }

        var references = new HashSet<(string File, string Name)>();
        foreach (var (file, root) in roots)
        {
            foreach (TypeDeclarationSyntax type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var matching = queries.Where(query => query.Type == type.Identifier.ValueText).ToArray();
                foreach (FieldDeclarationSyntax field in type.Members.OfType<FieldDeclarationSyntax>())
                {
                    bool isPublic = field.Modifiers.Any(SyntaxKind.PublicKeyword);
                    bool isStatic = field.Modifiers.Any(SyntaxKind.StaticKeyword) || field.Modifiers.Any(SyntaxKind.ConstKeyword);
                    if (!matching.Any(query => (isPublic ? query.Public : query.NonPublic) && (isStatic ? query.Static : query.Instance)))
                    {
                        continue;
                    }

                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    {
                        references.Add((Path.GetFullPath(file), variable.Identifier.ValueText));
                    }
                }
            }
        }

        return references;
    }
}
