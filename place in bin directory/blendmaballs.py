import bpy
import builtins
import png
import os
import sys 

def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

def create_mesh(triangle_data):
    vertices = []
    triangles = []

    # Split the triangle data and parse vertices
    for triangle_str in triangle_data:
        vertex_data = triangle_str.split()[1:4]
        triangle_vertices = []
        for vertex_str in vertex_data:
            vertex_coords = [float(coord) for coord in vertex_str.split(';')]
            vertices.append(vertex_coords)
            triangle_vertices.append(len(vertices) - 1)
        triangles.append(tuple(triangle_vertices))

    # Create mesh and object
    mesh = bpy.data.meshes.new(name="CustomMesh")
    obj = bpy.data.objects.new(name="CustomObject", object_data=mesh)

    # Link object to scene
    bpy.context.collection.objects.link(obj)

    # Create mesh data
    mesh.from_pydata(vertices, [], triangles)
    mesh.update()
    
    return obj

def mesh_to_triangle_data(obj):
    triangle_data = []

    # Get the object's mesh data
    mesh = obj.data

    # Iterate over the faces to reconstruct triangles
    for face in mesh.polygons:
        vertices = [mesh.vertices[vertex_index].co for vertex_index in face.vertices]
        triangle_str = "TRI " + " ".join(["{};{};{}".format(round(vertex[0]), round(vertex[1]), round(vertex[2])) for vertex in vertices])
        triangle_data.append(triangle_str)

    return triangle_data


def main():
    global objects
    global use_tri_params
    fname = "collisionData.txt"
    newfile = []
    
    with open(fname) as file:
        objects = {}
        use_tri_params = False
        
        def tri_list_done(objects):
            for key in objects:
                mesh_object = create_mesh(objects[key])
                bpy.context.view_layer.objects.active = mesh_object
                
                weld = mesh_object.modifiers.new("Weld", 'WELD')
                weld.merge_threshold = 2.5
                bpy.ops.object.modifier_apply(modifier="Weld")
                
                edgesplit = mesh_object.modifiers.new("EdgeSplit", 'EDGE_SPLIT')
                edgesplit.split_angle = 2/180*3.14159
                bpy.ops.object.modifier_apply(modifier="EdgeSplit")
                
                decimate = mesh_object.modifiers.new("Decimate", 'DECIMATE')
                decimate.decimate_type = "DISSOLVE"
                decimate.angle_limit = 2/180*3.14159
                bpy.ops.object.modifier_apply(modifier="Decimate")
                
                weld = mesh_object.modifiers.new("Weld", 'WELD')
                weld.merge_threshold = 2.5
                bpy.ops.object.modifier_apply(modifier="Weld")
                
                triangulate = mesh_object.modifiers.new("Triangulate", 'TRIANGULATE')
                bpy.ops.object.modifier_apply(modifier="Triangulate")
                
                triangle_data = mesh_to_triangle_data(mesh_object)
                for line in triangle_data:
                    ln = line
                    if use_tri_params:
                        ln += " " + str(key >> 8) + " " + str(key & 255)
                    newfile.append(ln)
            
            clear()
            objects.clear()
        
        for line in file:
            ln = line.strip()
            args = ln.split(" ")
            match args[0]:
                case "TRI":
                    if len(objects) == 0:
                        use_tri_params = len(args) > 5
                    
                    tri_params = 0
                    if use_tri_params:
                        tri_params = int(args[4]) * 256 + int(args[5])
                    
                    if not tri_params in objects:
                        objects[tri_params] = []
                    objects[tri_params].append(ln)
                case _:
                    print(ln)
                    tri_list_done(objects)
                    newfile.append(ln)
        
        tri_list_done(objects)
    
    with open(fname + ".new", "w") as file:
        for line in newfile:
            ln = line
            if ln.startswith("COLLISIONTYPE"):
                ln = "\t"+ln
            if ln.startswith("TRI"):
                ln = "\t\t"+ln
            file.write(ln+"\n")


if __name__ == "__main__":
    main()
