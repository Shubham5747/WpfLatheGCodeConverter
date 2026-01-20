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
