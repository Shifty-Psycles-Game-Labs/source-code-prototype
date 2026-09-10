@tool
extends EditorPlugin

var _importer: EditorImportPlugin

func _enter_tree() -> void:
	_importer = preload("res://addons/fcstd_importer/fcstd_importer.gd").new()
	add_import_plugin(_importer)

func _exit_tree() -> void:
	remove_import_plugin(_importer)
	_importer = null
