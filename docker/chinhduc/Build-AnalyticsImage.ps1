$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$buildRoot = Join-Path $temporaryRoot ('fanhub-analytics-build-' + [Guid]::NewGuid().ToString('N'))
try {
    # OneDrive can represent hydrated source files as reparse points which BuildKit rejects.
    # Materialize only the projects needed for this image, without cloud attributes.
    foreach ($project in @('services/dotnet/FanHub.AnalyticsService', 'services/dotnet/shared/FanHub.Shared.Contracts', 'services/dotnet/shared/FanHub.Shared.Common')) {
        $source = Join-Path $repository $project
        $files = Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            ($_.Extension -in '.cs','.csproj' -or $_.Name -in 'Dockerfile','appsettings.json','appsettings.Development.json')
        }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($repository.Length).TrimStart('\')
            $destination = Join-Path $buildRoot $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::WriteAllBytes($destination, [IO.File]::ReadAllBytes($file.FullName))
        }
    }
    [IO.File]::WriteAllBytes((Join-Path $buildRoot '.dockerignore'), [IO.File]::ReadAllBytes((Join-Path $repository '.dockerignore')))
    & docker build -t fanhub-chinhduc-analytics-service:local -f (Join-Path $buildRoot 'services/dotnet/FanHub.AnalyticsService/Dockerfile') $buildRoot
    if ($LASTEXITCODE -ne 0) { throw 'Analytics Docker image build failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($buildRoot)
    $allowedRoot = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^fanhub-analytics-build-[a-f0-9]{32}$') {
        throw 'Refusing to remove a directory outside the generated temporary build context.'
    }
    Remove-Item -LiteralPath $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
}
