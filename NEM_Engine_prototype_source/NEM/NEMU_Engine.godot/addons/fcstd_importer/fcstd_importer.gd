## fcstd_importer.gd
## EditorImportPlugin — converts .fcstd files to ArrayMesh resources.
##
## Pipeline:
##   1. Resolve the path to FreeCAD.exe (ProjectSettings key or hard-coded fallback).
##   2. Call FreeCAD.exe --console with fcstd_to_obj.py, passing source and a
##      temp .obj output path as arguments.
##   3. Parse the resulting .obj file with a minimal hand-written parser.
##   4. Build an ArrayMesh via SurfaceTool and save it as a .res resource.
##
## To override the FreeCAD executable path without editing this file, set:
##   ProjectSettings → application/config/freecad_exe
## to the full path of your FreeCAD.exe.
@tool
extends EditorImportPlugin

const _FREECAD_FALLBACK := "E:/ShiftyPsyclesGameLabs/ARCHIVE/ZIPs/UnZips/FreeCAD_1.1.3-Windows-x86_64-py311/FreeCAD_1.1.3-Windows-x86_64-py311/FreeCAD.exe"
const _HELPER_SCRIPT    := "res://addons/fcstd_importer/fcstd_to_obj.py"

# ---------------------------------------------------------------------------
# EditorImportPlugin interface
# ---------------------------------------------------------------------------

func _get_importer_name() -> String:
	return "fcstd_importer"

func _get_visible_name() -> String:
	return "FreeCAD FCStd Importer"

func _get_recognized_extensions() -> PackedStringArray:
	return PackedStringArray(["fcstd"])

func _get_save_extension() -> String:
	return "res"

func _get_resource_type() -> String:
	return "ArrayMesh"

func _get_preset_count() -> int:
	return 1

func _get_preset_name(_preset_index: int) -> String:
	return "Default"

func _get_import_options(_path: String, _preset_index: int) -> Array:
	return []

func _get_option_visibility(_path: String, _option_name: StringName, _options: Dictionary) -> bool:
	return true

func _get_import_order() -> int:
	return 0

func _get_priority() -> float:
	return 1.0

# ---------------------------------------------------------------------------
# Main import entry point
# ---------------------------------------------------------------------------

func _import(source_file: String, save_path: String, _options: Dictionary,
		_platform_variants: Array, _gen_files: Array) -> Error:

	# -- 1. Resolve FreeCAD executable path ---------------------------------
	var freecad_exe: String
	if ProjectSettings.has_setting("application/config/freecad_exe"):
		freecad_exe = ProjectSettings.get_setting("application/config/freecad_exe")
	else:
		freecad_exe = _FREECAD_FALLBACK

	freecad_exe = ProjectSettings.globalize_path(freecad_exe) \
			if freecad_exe.begins_with("res://") else freecad_exe

	if not FileAccess.file_exists(freecad_exe):
		push_error("FCStd Importer: FreeCAD.exe not found at '%s'. " \
				% freecad_exe +
				"Set application/config/freecad_exe in ProjectSettings.")
		return ERR_FILE_NOT_FOUND

	# -- 2. Build temp OBJ output path  ------------------------------------
	var global_source := ProjectSettings.globalize_path(source_file)
	var tmp_obj := global_source + ".tmp.obj"

	# -- 3. Resolve helper Python script ------------------------------------
	var helper_script := ProjectSettings.globalize_path(_HELPER_SCRIPT)
	if not FileAccess.file_exists(helper_script):
		push_error("FCStd Importer: helper script not found at '%s'." % helper_script)
		return ERR_FILE_NOT_FOUND

	# -- 4. Run FreeCAD.exe headlessly  ------------------------------------
	#   FreeCAD 1.x headless CLI:
	#     FreeCAD.exe --console -- <script> [args...]
	var args := PackedStringArray([
		"--console",
		"--",
		helper_script,
		global_source,
		tmp_obj,
	])

	var stdout := []
	var exit_code := OS.execute(freecad_exe, args, stdout, true, false)

	if exit_code != 0:
		push_error("FCStd Importer: FreeCAD.exe exited with code %d.\nOutput:\n%s"
				% [exit_code, "\n".join(PackedStringArray(stdout))])
		return FAILED

	if not FileAccess.file_exists(tmp_obj):
		push_error("FCStd Importer: FreeCAD did not produce '%s'.\nOutput:\n%s"
				% [tmp_obj, "\n".join(PackedStringArray(stdout))])
		return FAILED

	# -- 5. Parse .obj and build ArrayMesh ---------------------------------
	var mesh := _parse_obj_to_mesh(tmp_obj)

	# Clean up temp file (best-effort)
	DirAccess.remove_absolute(tmp_obj)

	if mesh == null:
		push_error("FCStd Importer: OBJ parsing failed for '%s'." % tmp_obj)
		return FAILED

	# -- 6. Save resource ---------------------------------------------------
	var full_save_path := save_path + ".res"
	var save_err := ResourceSaver.save(mesh, full_save_path)
	if save_err != OK:
		push_error("FCStd Importer: ResourceSaver.save() failed with error %d." % save_err)
		return save_err

	return OK

# ---------------------------------------------------------------------------
# Minimal OBJ parser → ArrayMesh
# ---------------------------------------------------------------------------
# Supports only the geometry subset FreeCAD's Mesh exporter writes:
#   v  <x> <y> <z>
#   f  <i> <j> <k>           (1-based vertex indices, triangles only)
#   f  <i>/... <j>/... <k>/... (with optional uv/normal indices, ignored)
#
# Quads and n-gons are fan-triangulated so the importer stays self-contained.

func _parse_obj_to_mesh(path: String) -> ArrayMesh:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return null

	var positions: Array[Vector3] = []
	var tri_indices: Array[int]   = []

	while not file.eof_reached():
		var raw := file.get_line().strip_edges()
		if raw.is_empty() or raw.begins_with("#"):
			continue

		var tokens := raw.split(" ", false)
		if tokens.is_empty():
			continue

		match tokens[0]:
			"v":
				if tokens.size() < 4:
					continue
				positions.append(Vector3(
					float(tokens[1]),
					float(tokens[2]),
					float(tokens[3])
				))

			"f":
				# Each token after "f" is  idx  or  idx/uv  or  idx/uv/nrm
				# OBJ indices are 1-based; negative values are relative.
				var n := tokens.size() - 1  # number of vertex refs on this face
				if n < 3:
					continue

				var face_verts: Array[int] = []
				for i in range(1, tokens.size()):
					var raw_idx := tokens[i].split("/")[0].to_int()
					# Resolve negative (relative) index
					if raw_idx < 0:
						raw_idx = positions.size() + raw_idx + 1
					face_verts.append(raw_idx - 1)  # convert to 0-based

				# Fan-triangulate
				for i in range(1, face_verts.size() - 1):
					tri_indices.append(face_verts[0])
					tri_indices.append(face_verts[i])
					tri_indices.append(face_verts[i + 1])

	file.close()

	if positions.is_empty() or tri_indices.is_empty():
		return null

	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	for idx in tri_indices:
		st.add_vertex(positions[idx])

	st.generate_normals()

	return st.commit()
