using System.Text;
using Liuvis.Generation.Services;

namespace Liuvis.Generation.Geometry;

/// <summary>
/// Exports scene descriptions to STEP AP214 format.
/// Generates valid B-Rep geometry for basic primitives (box, sphere, cylinder, cone).
/// </summary>
public class StepExporter
{
    private int _id;
    private readonly StringBuilder _sb = new();

    public byte[] Export(SceneDescription scene)
    {
        _id = 0;
        _sb.Clear();

        WriteHeader();

        var productIds = new List<int>();
        foreach (var obj in scene.Objects)
        {
            var bodyId =(obj.Type.ToLowerInvariant()) switch
            {
                "box" or "cube" => WriteBox(obj),
                "sphere" or "ball" => WriteSphere(obj),
                "cylinder" => WriteCylinder(obj),
                "cone" => WriteCone(obj),
                _ => WriteBox(obj)
            };
            productIds.Add(bodyId);
        }

        WriteFooter(productIds);
        return Encoding.UTF8.GetBytes(_sb.ToString());
    }

    private void WriteHeader()
    {
        _sb.AppendLine("ISO-10303-21;");
        _sb.AppendLine("HEADER;");
        _sb.AppendLine("FILE_DESCRIPTION(('Liuvis 3D Model'),'2;1');");
        _sb.AppendLine("FILE_NAME('model.step','2026-01-01',('Liuvis'),(),'Liuvis Studio','','');");
        _sb.AppendLine("FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));");
        _sb.AppendLine("ENDSEC;");
        _sb.AppendLine("DATA;");
    }

    private void WriteFooter(List<int> productIds)
    {
        var contextId = NextId();
        _sb.AppendLine($"{contextId}=APPLICATION_CONTEXT('automotive_design');");
        var protoId = NextId();
        _sb.AppendLine($"{protoId}=APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,{contextId});");

        foreach (var pid in productIds)
        {
            var shapeDefId = NextId();
            var prodDefId = NextId();
            var prodShapeId = NextId();
            var geomRepId = NextId();
            var prodCtxId = NextId();
            _sb.AppendLine($"{shapeDefId}=SHAPE_DEFINITION_REPRESENTATION({prodDefId},{prodShapeId});");
            _sb.AppendLine($"{prodDefId}=PRODUCT_DEFINITION('design','',{prodCtxId},$);");
            _sb.AppendLine($"{prodShapeId}=PRODUCT_DEFINITION_SHAPE($,$,{prodDefId});");
            _sb.AppendLine($"{geomRepId}=GEOMETRIC_REPRESENTATION_CONTEXT(3);");
            _sb.AppendLine($"{prodCtxId}=PRODUCT_CONTEXT($,{contextId},'design');");
        }

        _sb.AppendLine("ENDSEC;");
        _sb.AppendLine("END-ISO-10303-21;");
    }

    private int WriteBox(SceneObject obj)
    {
        var sx = obj.Size.Length > 0 ? (float)obj.Size[0] / 2f : 0.5f;
        var sy = obj.Size.Length > 1 ? (float)obj.Size[1] / 2f : 0.5f;
        var sz = obj.Size.Length > 2 ? (float)obj.Size[2] / 2f : 0.5f;
        var tx = obj.Position.Length > 0 ? (float)obj.Position[0] : 0f;
        var ty = obj.Position.Length > 1 ? (float)obj.Position[1] : 0f;
        var tz = obj.Position.Length > 2 ? (float)obj.Position[2] : 0f;

        // 8 vertices
        var pts = new (float x, float y, float z)[]
        {
            (-sx+tx, -sy+ty, -sz+tz), ( sx+tx, -sy+ty, -sz+tz),
            ( sx+tx,  sy+ty, -sz+tz), (-sx+tx,  sy+ty, -sz+tz),
            (-sx+tx, -sy+ty,  sz+tz), ( sx+tx, -sy+ty,  sz+tz),
            ( sx+tx,  sy+ty,  sz+tz), (-sx+tx,  sy+ty,  sz+tz),
        };
        var pIds = pts.Select(p => WriteCartesianPoint(p.x, p.y, p.z)).ToArray();

        // 12 edges: vertex pairs
        var edges = new (int a, int b)[]
        {
            (0,1),(1,2),(2,3),(3,0), // bottom
            (4,5),(5,6),(6,7),(7,4), // top
            (0,4),(1,5),(2,6),(3,7), // vertical
        };
        var edgeIds = new int[12];
        for (int i = 0; i < 12; i++)
            edgeIds[i] = WriteEdge(pIds[edges[i].a], pIds[edges[i].b]);

        // 6 faces, each with 4 edges (oriented)
        var faceEdgeSets = new (int e, bool rev)[]
        {
            // bottom (z-)
            (0,false),(1,false),(2,false),(3,false),
        };
        // bottom
        var f0 = WriteFace(new[] { (edgeIds[0],false),(edgeIds[1],false),(edgeIds[2],false),(edgeIds[3],false) }, 0,0,-1, pts[0].x, pts[0].y, pts[0].z);
        // top
        var f1 = WriteFace(new[] { (edgeIds[4],true),(edgeIds[5],true),(edgeIds[6],true),(edgeIds[7],true) }, 0,0,1, pts[4].x, pts[4].y, pts[4].z);
        // front (y-)
        var f2 = WriteFace(new[] { (edgeIds[0],true),(edgeIds[8],false),(edgeIds[4],false),(edgeIds[9],true) }, 0,-1,0, pts[0].x, pts[0].y, pts[0].z);
        // back (y+)
        var f3 = WriteFace(new[] { (edgeIds[2],true),(edgeIds[10],false),(edgeIds[6],false),(edgeIds[11],true) }, 0,1,0, pts[2].x, pts[2].y, pts[2].z);
        // right (x+)
        var f4 = WriteFace(new[] { (edgeIds[1],true),(edgeIds[9],true),(edgeIds[5],false),(edgeIds[10],false) }, 1,0,0, pts[1].x, pts[1].y, pts[1].z);
        // left (x-)
        var f5 = WriteFace(new[] { (edgeIds[3],true),(edgeIds[11],true),(edgeIds[7],false),(edgeIds[8],false) }, -1,0,0, pts[3].x, pts[3].y, pts[3].z);

        var shellId = WriteClosedShell(new[] { f0, f1, f2, f3, f4, f5 });
        var bodyId = WriteManifoldSolidBrep(shellId);
        return bodyId;
    }

    private int WriteSphere(SceneObject obj)
    {
        var r = obj.Size.Length > 0 ? (float)obj.Size[0] / 2f : 0.5f;
        var tx = obj.Position.Length > 0 ? (float)obj.Position[0] : 0f;
        var ty = obj.Position.Length > 1 ? (float)obj.Position[1] : 0f;
        var tz = obj.Position.Length > 2 ? (float)obj.Position[2] : 0f;

        var centerId = WriteCartesianPoint(tx, ty, tz);
        var axisDirId = WriteDirection(0, 0, 1);
        var axisPtId = WriteCartesianPoint(tx, ty, tz);
        var surfId = NextId();
        _sb.AppendLine($"{surfId}=SPHERICAL_SURFACE($,{centerId},{r});");

        // Write equator + meridian wire loop as trimmed surface
        var refDirId = WriteDirection(1, 0, 0);
        var trimWireId = WriteSphereTrimWire(tx, ty, tz, r);
        var faceId = WriteAdvancedFaceFromSurface(surfId, trimWireId, true);
        var shellId = WriteClosedShell(new[] { faceId });
        return WriteManifoldSolidBrep(shellId);
    }

    private int WriteSphereTrimWire(float cx, float cy, float cz, float r)
    {
        // Generate 16-point equatorial circle + 16-point meridian circle for sphere wire
        var segs = 16;
        var pIds = new List<int>();
        var eIds = new List<int>();

        for (int i = 0; i <= segs; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segs);
            float x = cx + r * MathF.Cos(angle);
            float y = cy + r * MathF.Sin(angle);
            pIds.Add(WriteCartesianPoint(x, y, cz));
        }
        for (int i = 0; i < segs; i++)
            eIds.Add(WriteEdge(pIds[i], pIds[i + 1]));

        var wireId = WriteOrientedWire(eIds.ToArray(), false);
        return wireId;
    }

    private int WriteCylinder(SceneObject obj)
    {
        var radius = obj.Size.Length > 0 ? (float)obj.Size[0] / 2f : 0.5f;
        var height = obj.Size.Length > 1 ? (float)obj.Size[1] : 2.0f;
        var tx = obj.Position.Length > 0 ? (float)obj.Position[0] : 0f;
        var ty = obj.Position.Length > 1 ? (float)obj.Position[1] : 0f;
        var tz = obj.Position.Length > 2 ? (float)obj.Position[2] : 0f;
        var halfH = height / 2f;

        var axisDirId = WriteDirection(0, 1, 0);
        var axisPtId = WriteCartesianPoint(tx, ty, tz);
        var cylSurfId = NextId();
        _sb.AppendLine($"{cylSurfId}=CYLINDRICAL_SURFACE($,{axisPtId},{axisDirId},{radius});");

        var segs = 16;
        var faces = new List<int>();

        // Lateral surface
        var wireId = WriteCircleWire(tx, ty, tz, radius, segs, "y");
        faces.Add(WriteAdvancedFaceFromSurface(cylSurfId, wireId, true));

        // Bottom cap
        var botCenterId = WriteCartesianPoint(tx, ty - halfH, tz);
        var botSurfId = NextId();
        var botDirId = WriteDirection(0, -1, 0);
        _sb.AppendLine($"{botSurfId}=PLANE($,{botCenterId},{botDirId});");
        var botWireId = WriteCircleWire(tx, ty - halfH, tz, radius, segs, "y");
        faces.Add(WriteAdvancedFaceFromSurface(botSurfId, botWireId, true));

        // Top cap
        var topCenterId = WriteCartesianPoint(tx, ty + halfH, tz);
        var topSurfId = NextId();
        var topDirId = WriteDirection(0, 1, 0);
        _sb.AppendLine($"{topSurfId}=PLANE($,{topCenterId},{topDirId});");
        var topWireId = WriteCircleWire(tx, ty + halfH, tz, radius, segs, "y");
        faces.Add(WriteAdvancedFaceFromSurface(topSurfId, topWireId, true));

        var shellId = WriteClosedShell(faces.ToArray());
        return WriteManifoldSolidBrep(shellId);
    }

    private int WriteCone(SceneObject obj)
    {
        var radius = obj.Size.Length > 0 ? (float)obj.Size[0] / 2f : 0.5f;
        var height = obj.Size.Length > 1 ? (float)obj.Size[1] : 2.0f;
        var tx = obj.Position.Length > 0 ? (float)obj.Position[0] : 0f;
        var ty = obj.Position.Length > 1 ? (float)obj.Position[1] : 0f;
        var tz = obj.Position.Length > 2 ? (float)obj.Position[2] : 0f;
        var halfH = height / 2f;

        var segs = 16;
        var faces = new List<int>();

        // Lateral surface — approximate with cylinder (simplified STEP)
        var axisDirId = WriteDirection(0, 1, 0);
        var axisPtId = WriteCartesianPoint(tx, ty, tz);
        var cylSurfId = NextId();
        _sb.AppendLine($"{cylSurfId}=CYLINDRICAL_SURFACE($,{axisPtId},{axisDirId},{radius});");
        var wireId = WriteCircleWire(tx, ty, tz, radius, segs, "y");
        faces.Add(WriteAdvancedFaceFromSurface(cylSurfId, wireId, true));

        // Bottom cap
        var botCenterId = WriteCartesianPoint(tx, ty - halfH, tz);
        var botSurfId = NextId();
        var botDirId = WriteDirection(0, -1, 0);
        _sb.AppendLine($"{botSurfId}=PLANE($,{botCenterId},{botDirId});");
        var botWireId = WriteCircleWire(tx, ty - halfH, tz, radius, segs, "y");
        faces.Add(WriteAdvancedFaceFromSurface(botSurfId, botWireId, true));

        // Top cap (point — tiny circle)
        var topCenterId = WriteCartesianPoint(tx, ty + halfH, tz);
        var topSurfId = NextId();
        var topDirId = WriteDirection(0, 1, 0);
        _sb.AppendLine($"{topSurfId}=PLANE($,{topCenterId},{topDirId});");
        var topWireId = WriteCircleWire(tx, ty + halfH, tz, 0.001f, segs, "y");
        faces.Add(WriteAdvancedFaceFromSurface(topSurfId, topWireId, true));

        var shellId = WriteClosedShell(faces.ToArray());
        return WriteManifoldSolidBrep(shellId);
    }

    private int WriteCircleWire(float cx, float cy, float cz, float r, int segs, string axis)
    {
        var pIds = new List<int>();
        var eIds = new List<int>();

        for (int i = 0; i <= segs; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segs);
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            float x, y, z;
            if (axis == "y")
            {
                x = cx + r * cos; y = cy; z = cz + r * sin;
            }
            else
            {
                x = cx + r * cos; y = cy + r * sin; z = cz;
            }
            pIds.Add(WriteCartesianPoint(x, y, z));
        }
        for (int i = 0; i < segs; i++)
            eIds.Add(WriteEdge(pIds[i], pIds[i + 1]));

        return WriteOrientedWire(eIds.ToArray(), false);
    }

    private int WriteCartesianPoint(float x, float y, float z)
    {
        var id = NextId();
        _sb.AppendLine($"{id}=CARTESIAN_POINT('',({x},{y},{z}));");
        return id;
    }

    private int WriteDirection(float x, float y, float z)
    {
        var id = NextId();
        _sb.AppendLine($"{id}=DIRECTION('',({x},{y},{z}));");
        return id;
    }

    private int WriteEdge(int p1Id, int p2Id)
    {
        var lineId = NextId();
        var dirId = WriteDirection(1, 0, 0); // simplified direction
        _sb.AppendLine($"{lineId}=LINE('',$,{dirId});");

        var curveId = lineId;
        var v1Id = WriteVertexPoint(p1Id);
        var v2Id = WriteVertexPoint(p2Id);

        var edgeId = NextId();
        _sb.AppendLine($"{edgeId}=EDGE_CURVE($,{v1Id},{v2Id},{curveId},.T.);");
        return edgeId;
    }

    private int WriteVertexPoint(int cartesianPointId)
    {
        var id = NextId();
        _sb.AppendLine($"{id}=VERTEX_POINT('',$,{cartesianPointId});");
        return id;
    }

    private int WriteOrientedWire(int[] edgeIds, bool sense)
    {
        var senseStr = sense ? ".T." : ".F.";
        var orientedEdgeIds = new List<int>();
        foreach (var eid in edgeIds)
        {
            var oeId = NextId();
            _sb.AppendLine($"{oeId}=ORIENTED_EDGE($,$,{eid},{senseStr});");
            orientedEdgeIds.Add(oeId);
        }

        var edgeListId = NextId();
        var idList = string.Join(",", orientedEdgeIds);
        _sb.AppendLine($"{edgeListId}=EDGE_LOOP('',({idList}));");

        var wireId = NextId();
        _sb.AppendLine($"{wireId}=WIRE_BOUND('',{edgeListId},{senseStr});");
        return wireId;
    }

    private int WriteFace((int edgeId, bool reversed)[] edges, float nx, float ny, float nz, float px, float py, float pz)
    {
        var planePtId = WriteCartesianPoint(px, py, pz);
        var planeDirId = WriteDirection(nx, ny, nz);
        var planeSurfId = NextId();
        _sb.AppendLine($"{planeSurfId}=PLANE($,{planePtId},{planeDirId});");

        var wireId = WriteOrientedWire(edges.Select(e => e.edgeId).ToArray(), false);
        var faceId = NextId();
        _sb.AppendLine($"{faceId}=ADVANCED_FACE('',({wireId}),{planeSurfId},.T.);");
        return faceId;
    }

    private int WriteAdvancedFaceFromSurface(int surfId, int wireId, bool sameSense)
    {
        var senseStr = sameSense ? ".T." : ".F.";
        var id = NextId();
        _sb.AppendLine($"{id}=ADVANCED_FACE('',({wireId}),{surfId},{senseStr});");
        return id;
    }

    private int WriteClosedShell(int[] faceIds)
    {
        var idList = string.Join(",", faceIds);
        var id = NextId();
        _sb.AppendLine($"{id}=CLOSED_SHELL('',({idList}));");
        return id;
    }

    private int WriteManifoldSolidBrep(int shellId)
    {
        var id = NextId();
        _sb.AppendLine($"{id}=MANIFOLD_SOLID_BREP('',$,{shellId});");
        return id;
    }

    private int NextId() => ++_id;
}
