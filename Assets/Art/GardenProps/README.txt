Stylized Garden Props (Free) v1.1
by Nickben12

16 free stylized garden props with baked PBR textures: picket fence, bench, shrubs, flower pot, raised bed, wheelbarrow, gnome and more. Game ready for Unity, Unreal and Godot.

CONTENTS (16 models)
  Plants & Flowers (4)
    Flower_Pot_01             1004 tris    0.29 x  0.29 x  0.51 m   1024 px
    Bush_Round_01              588 tris    1.08 x  0.97 x  0.83 m   1024 px
    Hedge_Block_01             432 tris    1.04 x  0.52 x  0.92 m   1024 px
    Grass_Tuft_01             1000 tris    0.56 x  0.57 x  0.41 m   512 px
  Vegetable Patch (3)
    Raised_Bed_01              782 tris    1.20 x  0.60 x  0.41 m   1024 px
    Pumpkin_01                 640 tris    0.40 x  0.40 x  0.34 m   1024 px
    Crate_Vegetables_01        682 tris    0.54 x  0.36 x  0.33 m   1024 px
  Tools (4)
    Watering_Can_01            684 tris    0.67 x  0.27 x  0.47 m   1024 px
    Wheelbarrow_01             764 tris    0.72 x  1.86 x  0.71 m   1024 px
    Shovel_01                  302 tris    0.22 x  0.05 x  1.14 m   1024 px
    Pots_Stack_01              840 tris    0.58 x  0.31 x  0.22 m   1024 px
  Garden Decor (2)
    Gnome_01                   610 tris    0.25 x  0.23 x  0.50 m   1024 px
    Birdhouse_01               112 tris    0.32 x  0.29 x  1.66 m   1024 px
  Fences & Furniture (3)
    Fence_Picket_2m_01         308 tris    2.01 x  0.10 x  1.09 m   1024 px
    Fence_Picket_Post_01        64 tris    0.10 x  0.10 x  1.09 m   512 px
    Bench_Garden_01            988 tris    1.40 x  0.64 x  0.93 m   1024 px

MORE
  This is the free version. The Extra version has all 44 models (28 more) and the Blender source file: https://nickben12.itch.io/stylized-garden-props

FOLDERS
  FBX/                  one .fbx per model (Unity, Unreal, most engines)
  GLB/                  one .glb per model, PBR material and textures embedded (Godot, three.js...)
  Textures/             PBR textures of every model (metallic / roughness workflow):
                          <Model>_BaseColor.png           albedo (sRGB)
                          <Model>_Normal.png              tangent-space normal map, OpenGL (Y+)
                          <Model>_Roughness.png           roughness (linear)
                          <Model>_Metallic.png            metallic (linear)
                          <Model>_MetallicSmoothness.png  Unity mask: metallic RGB, smoothness alpha
  Previews/             a preview image of every model

TECHNICAL NOTES
  - Real-world scale: 1 unit = 1 metre.
  - Pivot at the bottom centre of every model: it sits on the ground at height 0.
  - Front of every model faces -Y in Blender (-Z in Unity / Godot after import).
  - Picket fence: modular on a 2 m grid. Each piece has its post on its left end
    (X = -1 m for Fence_Picket_2m_01, X = -0.5 m for the 1 m gate). Snap pieces end to
    end and close the run with Fence_Picket_Post_01.
  - Tools (shovel, rake, pitchfork) stand upright on their working end: rotate them
    to lean them against a wall or lay them on the ground.
  - The lantern glass is painted warm: add an emissive map or a point light in your
    engine for a glowing look.
  - Every model: one mesh, one material, one texture set (512 to 2048 px). Detail
    (leaves, petals, wood grain, chipped paint, stone, straw) is baked from high-poly
    sources into the normal and colour maps: light meshes, rich look.

IMPORT TIPS
  Unity   : drag the FBX and Textures folders into Assets. In the material, use BaseColor
            as Albedo / Base Map, Normal as Normal Map (set its texture type to Normal map)
            and MetallicSmoothness as Metallic map (smoothness source: Metallic alpha).
  Unreal  : import the FBX files, then the textures. Normal maps are OpenGL style: tick
            'Flip Green Channel' on the *_Normal textures.
  Godot 4 : drop the GLB files into your project, materials are set up automatically.

LICENSE
  See LICENSE.txt. Thank you for downloading!
