# Codex .NET Environment – AGENTS.md
Updated 23 May 2025

---

## Important: Offline Environment
The Codex .NET environment is fully offline. There is **no internet access**. Therefore:
- **Do not attempt to fetch dependencies or resources from the internet.**
- All NuGet packages must already be cached locally in `$HOME/.nuget/offline-cache`.
- The `nuget.config` should not be committed to the repository and is only useful in the Codex environment.
- External API calls or network requests are not possible during development.

---

## Essential Workflow Rules

1. **Build Project Files Only**  
   Always specify a path to a specific `.csproj`. Never build `.sln` solution files directly.

2. **Run Tests Only When Present**  
   Skip test execution entirely if no test projects exist. Do not attempt to create or scaffold new test projects.

3. **Validate Every Code Change**  
   Follow these steps for each modification:
   - `dotnet build <Project.csproj>` – ensure code compiles correctly.
   - `dotnet format` or `csharpier .` – fix code style and lint errors.
   - When functionality verification is needed, use `dotnet run --project <Project.csproj>` or debug directly.

4. **Handle Build Failures Gracefully**  
   If builds fail due to missing packages, check the offline cache and suggest package restoration steps.

---

## Installed SDK Versions
- **.NET 9** (Current)
- **.NET 8 LTS** (Long Term Support)

Both installed at `$DOTNET_ROOT` (`$HOME/.dotnet` by default).

**Verify installation:**
bash
dotnet --list-sdks
dotnet --info


---

## Environment Variables
bash
DOTNET_ROOT=$HOME/.dotnet
PATH=$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH
NUGET_PACKAGES=$HOME/.nuget/offline-cache

# Performance & UX
MSBUILDTERMINALLOGGER=off
DOTNET_USE_POLLING_FILE_WATCHER=1
DOTNET_CLI_TELEMETRY_OPTOUT=1
DOTNET_NOLOGO=1
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
MSBUILDDISABLENODEREUSE=1

# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=https://*:5001;http://*:5000
DOTNET_GENERATE_ASPNET_CERTIFICATE=false

# Offline-specific
DisableOpenApiSchemaDownload=true


---

## Common CLI Commands

### Building & Running
bash
# Build specific project
dotnet build <Project.csproj> --no-restore -c Release

# Run project with hot reload
dotnet watch run --project <Project.csproj>

# Run without build
dotnet run --project <Project.csproj> --no-build

# Restore packages (offline cache only)
dotnet restore --packages $NUGET_PACKAGES --verbosity minimal


### Package Management
bash
# List installed packages
dotnet list <Project.csproj> package

# Check for outdated packages (offline cache only)
dotnet outdated

# Add package reference
dotnet add <Project.csproj> package <PackageName>


### Testing
bash
# Run tests (if test projects exist)
dotnet test --no-build --verbosity normal

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "TestName"


---

## Code Formatting and Linting

### Built-in Tools
bash
# Format code using built-in formatter
dotnet format --verbosity diagnostic

# Format specific project
dotnet format <Project.csproj>

# Analyze code without fixing
dotnet format --verify-no-changes --verbosity diagnostic


### CSharpier (Enhanced Formatting)
bash
# Format all files
csharpier .

# Check formatting without changes
csharpier . --check

# Format specific files
csharpier **/*.cs


### Additional Analysis
bash
# Security analysis
dotnet list package --vulnerable

# Package dependency tree
dotnet list package --include-transitive


---

## Entity Framework Core Commands

### Migrations
bash
# Create new migration
dotnet ef migrations add <MigrationName> --project <DataProject.csproj>

# Remove last migration
dotnet ef migrations remove --project <DataProject.csproj>

# List migrations
dotnet ef migrations list --project <DataProject.csproj>


### Database Operations
bash
# Update database to latest migration
dotnet ef database update --project <DataProject.csproj>

# Update to specific migration
dotnet ef database update <MigrationName> --project <DataProject.csproj>

# Generate SQL script
dotnet ef migrations script --project <DataProject.csproj>

# Drop database
dotnet ef database drop --project <DataProject.csproj>


---

## Diagnostics & Profiling

### Process Diagnostics
bash
# List .NET processes
dotnet-trace ps

# Collect memory dump
dotnet-dump collect -p <PID> -o dump.dmp

# Analyze dump
dotnet-dump analyze dump.dmp


### Performance Tracing
bash
# Collect 60-second trace
dotnet-trace collect -p <PID> --duration 00:01:00 -o trace.nettrace

# Collect specific providers
dotnet-trace collect -p <PID> --providers Microsoft-AspNetCore-Server-Kestrel

# Convert trace to other formats
dotnet-trace convert trace.nettrace --format chromium


### Live Monitoring
bash
# Monitor performance counters
dotnet-counters monitor -p <PID>

# Monitor specific counters
dotnet-counters monitor -p <PID> --counters System.Runtime[cpu-usage,working-set]

# All-in-one monitoring (if available)
dotnet-monitor collect --urls http://localhost:52323


---

## OpenAPI Client Generation

### NSwag Configuration
bash
# Generate client from .nswag file
nswag run config.nswag

# Generate with specific runtime
nswag run config.nswag /runtime:Net80

# Validate configuration
nswag validate config.nswag


### Manual Generation
bash
# Generate from OpenAPI spec file
nswag openapi2csclient /input:swagger.json /output:ApiClient.cs


---

## HTTPS Development Certificate

The development environment includes a trusted HTTPS certificate for:
- `https://localhost:5001`
- `https://*:5001` (all interfaces)

### Certificate Management
bash
# List certificates
dotnet dev-certs https --check

# Clean and recreate
dotnet dev-certs https --clean
dotnet dev-certs https --trust

# Export certificate (Linux/WSL)
dotnet dev-certs https --format PEM --export-path aspnet-dev.crt --no-password


---

## Platform-Specific Considerations

### Windows-Targeted Projects
bash
# Build Windows-specific projects
dotnet restore -p:EnableWindowsTargeting=true
dotnet build <Project.csproj> -p:EnableWindowsTargeting=true


### Linux/WSL Considerations
bash
# Install certificates manually if needed
sudo cp aspnet-dev.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates


---

## Unsupported Workloads

Projects targeting the following platforms will be automatically skipped:
- **Mobile:** `Android`, `Droid`, `iOS`
- **Desktop:** `MacCatalyst`, `WinUI`, `Tizen`
- **Legacy:** Xamarin.* projects

Required workloads are unavailable in the offline environment.

---

## NuGet Configuration

The offline environment uses a custom `nuget.config`:
xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline-cache" value="$HOME/.nuget/offline-cache" />
    <add key="local-packages" value="./packages" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="$HOME/.nuget/offline-cache" />
  </config>
</configuration>


**Important:** This file should not be committed to source control.

---

## Codex Task Best Practices

### Development Workflow
1. **Single Project Focus**: Work on individual projects rather than entire solutions for better Codex efficiency.
2. **Incremental Changes**: Make small, focused modifications with frequent validation.
3. **Always Validate**: End each significant change with build and format operations.

### Error Handling
1. **Full Context**: Always provide complete stack traces and error messages in prompts.
2. **Build First**: Run `dotnet build` before attempting to run or test.
3. **Check Dependencies**: Verify all required packages are available in the offline cache.

### Code Quality
bash
# Recommended validation sequence
dotnet build <Project.csproj>
dotnet format <Project.csproj>
csharpier .
dotnet test # (if tests exist)


### Debugging Strategy
1. **Use Built-in Tools**: Prefer `dotnet run` with debug output over external debugging tools.
2. **Console Output**: Add strategic `Console.WriteLine()` or `ILogger` statements for troubleshooting.
3. **Hot Reload**: Use `dotnet watch` for rapid iteration during development.

### Performance Considerations
- Use `--no-restore` flag when packages are already cached
- Specify `--no-build` when running recently built projects
- Use `--verbosity minimal` to reduce output noise
- Enable `MSBUILDDISABLENODEREUSE=1` to prevent MSBuild process issues

---

## Troubleshooting Common Issues

### Package Restore Failures
bash
# Clear local cache and retry
dotnet nuget locals all --clear
dotnet restore --force --no-cache


### Build Performance Issues
bash
# Clean and rebuild
dotnet clean <Project.csproj>
dotnet build <Project.csproj> --no-incremental


### Certificate Issues
bash
# Reset HTTPS development certificate
dotnet dev-certs https --clean
dotnet dev-certs https --trust


### File Watcher Problems
bash
# Use polling instead of native file watching
export DOTNET_USE_POLLING_FILE_WATCHER=1
dotnet watch run --project <Project.csproj>


---

## Quick Reference Commands

bash
# Essential development cycle
dotnet build <Project.csproj> --no-restore
dotnet format <Project.csproj>
dotnet run --project <Project.csproj> --no-build

# Package management
dotnet list <Project.csproj> package
dotnet add <Project.csproj> package <PackageName>

# Testing
dotnet test --no-build --logger "console;verbosity=normal"

# Diagnostics
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes