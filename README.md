# WpfLatheGCodeConverter (prototype)

This is a scaffold for a WPF (.NET 8) desktop app that imports SVG/DXF/STEP/images, generates turning G-code (lathe) with multi-tool support, and provides a visual-only simulation.

Notes
- Target: .NET 8 (net8.0-windows), WPF.
- External tools (bundled or user-provided):
  - Potrace (for PNG/JPG -> SVG tracing) — place `potrace.exe` in the ./tools folder.
  - FreeCAD (for STEP -> DXF/SVG conversion) — place `freecadcmd.exe` in ./tools or install FreeCAD and add to PATH.
- Packaging: you can create a zip of the project folder for distribution. Be mindful of third-party licenses when bundling native tools.

How to build
1. Install .NET 8 SDK.
2. Open a terminal in the project folder and run:
   - dotnet restore
   - dotnet build
3. Run the app in Visual Studio or with the CLI:
   - dotnet run --project WpfLatheGCodeConverter.csproj

## Python ezdxf normalizer

The application includes a Python-based DXF normalizer (`tools/python/ezdxf_normalize.py`) that uses the [ezdxf](https://github.com/mozman/ezdxf) library to read various DXF versions, including those not fully supported by netDxf.

### Setup

1. **Install Python 3**: Ensure Python 3.x is installed and available in your PATH. You can download it from [python.org](https://www.python.org/downloads/).

2. **Install ezdxf**: Run the following command in your terminal:
   ```bash
   pip install ezdxf
   ```

### How it works

When importing a DXF file, the application will:
1. First try to use the netDxf library (C#-based, fast, but limited DXF version support)
2. If that fails or returns no geometry, fall back to the ezdxf wrapper (Python-based, supports more DXF versions)
3. If both fail, attempt a basic ASCII DXF parser for R12 files

The ezdxf wrapper writes diagnostic files to your system's temporary folder:
- `{filename}_ezdxf.json` - Normalized geometry data
- `{filename}_ezdxf.log` - Verbose processing log

### Manual usage (debugging)

To run the normalizer script manually for debugging:

```bash
python tools/python/ezdxf_normalize.py input.dxf output.json --approx-segs 48 --explode-inserts --verbose
```

Parameters:
- `input.dxf` - Input DXF file path
- `output.json` - Output JSON file path
- `--approx-segs N` - Number of segments for arc/circle approximation (default: 36)
- `--explode-inserts` - Expand BLOCK INSERT entities into geometry
- `--verbose` - Print detailed processing information to stderr

### Troubleshooting

**Script not found**: By default, the application searches for the script in the output directory (`bin/Debug/net8.0-windows/tools/python/ezdxf_normalize.py`). If you need to override this, set the `EZDXF_SCRIPT_PATH` environment variable to point to the script file:

```bash
set EZDXF_SCRIPT_PATH=C:\path\to\ezdxf_normalize.py
```

**Python not found**: Ensure Python is in your system PATH. The application tries `python`, `python3`, and the Windows `py` launcher in that order.

**Missing ezdxf module**: Install it with `pip install ezdxf` as shown above.

**Diagnostic files location**: After importing a DXF file, use the "Diagnostics" button to view the paths to the generated JSON and log files, or check your system's temporary folder (%TEMP% on Windows, /tmp on Unix).

