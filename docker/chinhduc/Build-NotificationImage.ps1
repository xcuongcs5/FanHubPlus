$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$buildRoot = Join-Path $temporaryRoot ('fanhub-notification-build-' + [Guid]::NewGuid().ToString('N'))
try {
    # OneDrive can represent hydrated source files as reparse points which BuildKit rejects.
    # Materialize only the two projects needed for this image, without cloud attributes.
    foreach ($project in @('services/dotnet/FanHub.NotificationService', 'services/dotnet/shared/FanHub.Shared.Contracts')) {
        $source = Join-Path $repository $project
        $files = Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            ($_.Extension -in '.cs','.csproj' -or $_.Name -in 'Dockerfile','appsettings.json','appsettings.Development.json')
        }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($repository.Length).TrimStart('\', '/')
            $destination = Join-Path $buildRoot $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::WriteAllBytes($destination, [IO.File]::ReadAllBytes($file.FullName))
        }
    }
    [IO.File]::WriteAllBytes((Join-Path $buildRoot '.dockerignore'), [IO.File]::ReadAllBytes((Join-Path $repository '.dockerignore')))
    & docker build -t fanhub-chinhduc-notification-service:local -f (Join-Path $buildRoot 'services/dotnet/FanHub.NotificationService/Dockerfile') $buildRoot
    if ($LASTEXITCODE -ne 0) { throw 'Notification Docker image build failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($buildRoot)
    $allowedRoot = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^fanhub-notification-build-[a-f0-9]{32}$') {
        throw 'Refusing to remove a directory outside the generated temporary build context.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
