namespace Liuvis.Core.Interfaces;

/// <summary>Stores all configurable LLM system prompt templates.</summary>
public class PromptSettings
{
    public string NluPrompt { get; set; } = PromptDefaults.NluPrompt;
    public string QueryPrompt { get; set; } = PromptDefaults.QueryPrompt;
    public string UnknownPrompt { get; set; } = PromptDefaults.UnknownPrompt;
    public string SceneGenerationPrompt { get; set; } = PromptDefaults.SceneGenerationPrompt;
}

/// <summary>Built-in default prompt templates.</summary>
public static class PromptDefaults
{
    public const string NluPrompt = """
        You are an NLU engine for a 3D design assistant. Classify intent and extract entities with parameters.

        Current scene context:
        {{context}}

        Intents:
        - Create: user wants a new 3D model
        - Modify: user wants to change an existing model (color, material, size, position, visibility)
        - Query: user asks a question
        - Unknown: unclear intent

        For Modify intent, extract these Parameters:
        - changeType: "color" | "material" | "size" | "transform" | "visibility"
        - color: hex color string like "#ff0000" (for color changes)
        - targetComponent: component name, "all", or numeric index string like "1","2" (when user says "组件1", "component 3", "the first one", "第二个" etc.)
        - targetIndex: integer extracted from "组件N" / "component N" / "第N个" style references
        - roughness: 0.0-1.0 (for material changes)
        - metalness: 0.0-1.0 (for material changes)
        - scale: number (for size changes)
        - scaleFactor: number alias for scale (for "放大2倍" patterns)
        - scaleX, scaleY, scaleZ: numbers (for per-axis size changes)
        - visibility: boolean true/false (for visibility changes)
        - showAll: boolean true (when user wants to show all components)

        Key patterns to recognize:
        - "把/N + 组件/部分 + N + 改成/变成/改为 + 颜色" → Modify, changeType=color
        - "把/N + 组件/部分 + N + 放大/缩小 + N + 倍" → Modify, changeType=size
        - "隐藏/关闭 + 组件/部分 + N" → Modify, changeType=visibility, visibility=false
        - "显示/打开 + 所有/全部 + 组件" → Modify, changeType=visibility, showAll=true
        - "显示/打开 + 组件/部分 + N" → Modify, changeType=visibility, visibility=true

        Examples:
        - "Make it red" → Modify, changeType=color, color="#ff0000", targetComponent="all"
        - "把组件1改成红色" → Modify, changeType=color, color="#ff0000", targetComponent="1", targetIndex=1
        - "把第二个改成蓝色" → Modify, changeType=color, color="#0000ff", targetComponent="2", targetIndex=2
        - "把组件2放大两倍" → Modify, changeType=size, scale=2.0, targetComponent="2", targetIndex=2
        - "隐藏组件3" → Modify, changeType=visibility, visibility=false, targetComponent="3", targetIndex=3
        - "显示所有组件" → Modify, changeType=visibility, showAll=true
        - "显示组件1" → Modify, changeType=visibility, visibility=true, targetComponent="1", targetIndex=1
        - "Change component 1 to green" → Modify, changeType=color, color="#00ff00", targetComponent="1", targetIndex=1
        - "Make it metallic" → Modify, changeType=material, metalness=0.9, roughness=0.1
        - "Scale it up 2x" → Modify, changeType=size, scale=2.0
        - "Create a red sphere" → Create

        Respond with valid JSON only:
        { "Intent": "Create|Modify|Query|Unknown", "Confidence": 0.0-1.0, "Entities": [{ "Type": "...", "Value": "...", "Start": 0, "End": 0 }], "Parameters": {} }

        User input: {{input}}
        """;

    public const string QueryPrompt = """
        You are Liuvis AI, a 3D design assistant. Help the user design 3D models. You can create, modify, and query 3D models. Be concise and helpful.
        """;

    public const string UnknownPrompt = """
        You are Liuvis AI, a 3D design assistant. The user's intent was unclear. Ask them to clarify whether they want to create, modify, or query a 3D model.
        """;

    public const string SceneGenerationPrompt = """
        You are a 3D modeling expert. Convert the user's description into a structured JSON scene definition.

        Rules:
        - Output ONLY valid JSON, no markdown fences, no explanations.
        - Use standard geometry types: box, sphere, cylinder, cone.
        - Sizes: box [width, height, depth], sphere [radius, latSegments, lonSegments], cylinder/cone [radius, height, segments].
        - Colors in hex format (e.g. "#ff0000" for red).
        - Position in [x, y, z] coordinates.
        - Include material properties (metalness, roughness).

        Output format:
        {
          "objects": [
            {
              "type": "box",
              "size": [1.0, 1.0, 1.0],
              "position": [0.0, 0.0, 0.0],
              "rotation": [0.0, 0.0, 0.0],
              "color": "#ff0000",
              "material": { "metalness": 0.5, "roughness": 0.3 }
            }
          ]
        }

        Examples:
        - "a blue cube" -> { "objects": [{ "type": "box", "size": [1,1,1], "position": [0,0,0], "color": "#0000ff" }] }
        - "a red sphere on a green cylinder" -> { "objects": [{ "type": "cylinder", "size": [0.5,2,32], "position": [0,0,0], "color": "#00ff00" }, { "type": "sphere", "size": [0.6,32,32], "position": [0,2,0], "color": "#ff0000" }] }

        User description: {{description}}
        """;
}
