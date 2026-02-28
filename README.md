# ShittyMaze - 3D First-Person Maze Game

A simple 3D first-person maze game built with KNI (XNA) framework for HTML5/Web.

to play on 5012:

```bash
dotnet publish .\Web\ShittyMaze.Web.csproj -c Release -f net8.0 /p:TargetPlatform=WebGL
cd .\Web\bin\Release\net8.0\publish\wwwroot\
python -m http.server 5012
```