namespace Liuvis.NLU.Models;

/// <summary>Handlebars-based prompt template for NLU operations.</summary>
public class NluPromptTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public Dictionary<string, object> Variables { get; set; } = new();

    public string Render()
    {
        var compiled = HandlebarsDotNet.Handlebars.Compile(Template);
        return compiled(Variables);
    }
}
