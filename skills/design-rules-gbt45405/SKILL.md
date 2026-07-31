---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 56ab3647c00a3b3fdf8fade204e7a8ee_327d141f8cbc11f19986525400287e28
    ReservedCode1: 1p1076NWRnzLaSzpgGzGA1LQgBo6uWYsEpcTmN5Ym40Dxtf3LU4T4qCRcC2ICGuKoC9XTfF5/LAeouToJ9CD5EGtPPaKvm+SLUsiF3EuDhU4HH3GMwkfKAbIWs5Wui7xQysV7B5BXrWl2isgJytOMridFB1zlNt0IlLX/XHzzf0INZYwaHPGSNi+aWU=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 56ab3647c00a3b3fdf8fade204e7a8ee_327d141f8cbc11f19986525400287e28
    ReservedCode2: 1p1076NWRnzLaSzpgGzGA1LQgBo6uWYsEpcTmN5Ym40Dxtf3LU4T4qCRcC2ICGuKoC9XTfF5/LAeouToJ9CD5EGtPPaKvm+SLUsiF3EuDhU4HH3GMwkfKAbIWs5Wui7xQysV7B5BXrWl2isgJytOMridFB1zlNt0IlLX/XHzzf0INZYwaHPGSNi+aWU=
---



# 设计规则：GB/T 45405 三维模型合规校验

本技能提供基于 GB/T 45405 系列标准的三维模型设计方案合规校验能力。

## 使用方式

1. 当 ModelingAgent 生成 SceneDescription 后，调用 `load_skill("design-rules-gbt45405")` 加载本技能。
2. 将生成的 SceneDescription JSON 与校验指令拼接为 Prompt，交由 LLM 依本指南逐项检查。

## 校验项

### 1. 几何精度
- 线性尺寸标注应保留至 0.1mm（企业级精度）
- 角度标注保留至 0.1°
- 建模样品中 geometry dimension 数值应落在合理工程范围

### 2. 建模规范
- 基坐标系原点默认 [0, 0, 0]
- 对象 Position 偏移应避免与已有组件碰撞（components 间 bounding box 不得重叠）
- 旋转 Rotation 使用欧拉角 [pitch, yaw, roll] 弧度制

### 3. 材料属性
- Metallic 材料 metalness ≥ 0.8, roughness ≤ 0.3
- 非金属材料 metalness ≤ 0.2, roughness ∈ [0.4, 0.9]
- 颜色标注使用 hex 6 位格式（如 #ff0000），禁用颜色名称

### 4. 结构完整性
- 每个 SceneDescription 至少包含 1 个 object
- 每个 object 必须包含 type / size / position 三要素
- size 数组维度必须与 geometry type 匹配：
  - box: [width, height, depth]，3 个元素
  - sphere: [radius, latSegments, lonSegments]，3 个元素
  - cylinder: [radius, height, segments]，3 个元素
  - cone: [radius, height, segments]，3 个元素

## 输出格式

校验完成后以 JSON 格式输出：

```json
{
  "passed": true,
  "total_checks": 8,
  "violations": [
    {
      "check": "几何精度",
      "object_index": 0,
      "field": "size[0]",
      "message": "...",
      "suggestion": "..."
    }
  ]
}
```
*（内容由AI生成，仅供参考）*
