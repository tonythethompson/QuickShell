#Requires -Version 5.1
<#
.SYNOPSIS
    Pester coverage for scripts/extract-changelog.ps1 validation branches.
#>

BeforeAll {
    $script:ScriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'extract-changelog.ps1'
    $script:FixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("qs-changelog-tests-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:FixtureRoot | Out-Null
}

AfterAll {
    if ($script:FixtureRoot -and (Test-Path -LiteralPath $script:FixtureRoot)) {
        Remove-Item -LiteralPath $script:FixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'extract-changelog.ps1' {
    BeforeEach {
        $script:FixturePath = Join-Path $script:FixtureRoot ("changelog-" + [guid]::NewGuid().ToString('N') + '.md')
    }

    AfterEach {
        if (Test-Path -LiteralPath $script:FixturePath) {
            Remove-Item -LiteralPath $script:FixturePath -Force -ErrorAction SilentlyContinue
        }
    }

    It 'throws for a malformed version' {
        Set-Content -LiteralPath $script:FixturePath -Value "## [0.2.3.0]`n`n- notes" -Encoding utf8

        { & $script:ScriptPath -Version 'not-a-version' -Path $script:FixturePath } |
            Should -Throw -ExpectedMessage '*Invalid version*'
    }

    It 'throws when the changelog file is missing' {
        $missing = Join-Path $script:FixtureRoot 'does-not-exist.md'

        { & $script:ScriptPath -Version '0.2.3.0' -Path $missing } |
            Should -Throw -ExpectedMessage '*CHANGELOG not found*'
    }

    It 'throws when the version section is missing' {
        Set-Content -LiteralPath $script:FixturePath -Value @"
# Changelog

## [0.1.0.0] - 2026-01-01

- Older notes
"@ -Encoding utf8

        { & $script:ScriptPath -Version '0.2.3.0' -Path $script:FixturePath } |
            Should -Throw -ExpectedMessage '*No CHANGELOG section*'
    }

    It 'throws when the version section is empty' {
        Set-Content -LiteralPath $script:FixturePath -Value @"
# Changelog

## [0.2.3.0] - 2026-07-28

## [0.1.0.0] - 2026-01-01

- Older notes
"@ -Encoding utf8

        { & $script:ScriptPath -Version '0.2.3.0' -Path $script:FixturePath } |
            Should -Throw -ExpectedMessage '*is empty*'
    }

    It 'throws when the version section is placeholder-only' {
        Set-Content -LiteralPath $script:FixturePath -Value @"
# Changelog

## [0.2.3.0] - 2026-07-28

TBD

## [0.1.0.0] - 2026-01-01

- Older notes
"@ -Encoding utf8

        { & $script:ScriptPath -Version '0.2.3.0' -Path $script:FixturePath } |
            Should -Throw -ExpectedMessage '*placeholder*'
    }

    It 'prints the section body on successful extraction' {
        Set-Content -LiteralPath $script:FixturePath -Value @"
# Changelog

## [0.2.3.0] - 2026-07-28

### Added

- First workspace trust prompt

### Fixed

- Launch path quoting

## [0.1.0.0] - 2026-01-01

- Older notes
"@ -Encoding utf8

        $notes = & $script:ScriptPath -Version '0.2.3.0' -Path $script:FixturePath

        ($notes -join "`n") | Should -Match 'First workspace trust prompt'
        ($notes -join "`n") | Should -Match 'Launch path quoting'
        ($notes -join "`n") | Should -Not -Match 'Older notes'
    }
}
