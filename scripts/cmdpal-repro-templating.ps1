# Standalone check: AdaptiveCards templating layer for $when + DataJson (no CmdPal UI)
# Confirms whether expanded card JSON includes conditional elements after DataJson update.

$ErrorActionPreference = 'Stop'

$templateJson = @'
{
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "type": "AdaptiveCard",
  "version": "1.6",
  "body": [
    {
      "type": "Input.ChoiceSet",
      "id": "Mode",
      "style": "compact",
      "value": "${Mode}",
      "choices": [
        { "title": "Off", "value": "off" },
        { "title": "Custom (requires file)", "value": "custom" }
      ]
    },
    {
      "type": "TextBlock",
      "$when": "${ShowWarning}",
      "text": "Pick a file before saving.",
      "color": "Attention",
      "wrap": true
    },
    {
      "type": "TextBlock",
      "text": "Status: ${Status}",
      "wrap": true,
      "isSubtle": true
    }
  ],
  "actions": [
    {
      "type": "Action.Submit",
      "title": "Apply",
      "associatedInputs": "auto"
    }
  ]
}
'@

$initialData = '{"Mode":"off","ShowWarning":false,"Status":"ShowWarning=false"}'
$afterCustom = '{"Mode":"custom","ShowWarning":true,"Status":"ShowWarning=true"}'

$projDir = Join-Path $env:TEMP 'cmdpal-repro-templating'
$projFile = Join-Path $projDir 'Repro.csproj'
if (-not (Test-Path $projFile)) {
    New-Item -ItemType Directory -Path $projDir -Force | Out-Null
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AdaptiveCards.Templating" Version="2.0.0" />
  </ItemGroup>
</Project>
'@ | Set-Content -Path $projFile -Encoding UTF8

    @'
using AdaptiveCards.Templating;

var templateJson = File.ReadAllText(args[0]);
var dataJson = File.ReadAllText(args[1]);
var template = new AdaptiveCardTemplate(templateJson);
var expanded = template.Expand(dataJson);
Console.WriteLine(expanded);
'@ | Set-Content -Path (Join-Path $projDir 'Program.cs') -Encoding UTF8
}

$templateFile = Join-Path $env:TEMP 'when-repro-template.json'
$initialFile = Join-Path $env:TEMP 'when-repro-initial.json'
$afterFile = Join-Path $env:TEMP 'when-repro-after.json'
$templateJson | Set-Content $templateFile -Encoding UTF8
$initialData | Set-Content $initialFile -Encoding UTF8
$afterCustom | Set-Content $afterFile -Encoding UTF8

Write-Host '=== Issue 1: templating layer (AdaptiveCards.Templating) ===' -ForegroundColor Cyan
Write-Host 'Initial DataJson expand:'
$initialExpanded = dotnet run --project $projFile -- $templateFile $initialFile 2>$null
Write-Host $initialExpanded
$initialHasWarning = $initialExpanded -match 'Pick a file before saving'
Write-Host "Contains warning text: $initialHasWarning (expected False)" -ForegroundColor $(if (-not $initialHasWarning) { 'Green' } else { 'Red' })

Write-Host ''
Write-Host 'After custom Apply DataJson expand:'
$afterExpanded = dotnet run --project $projFile -- $templateFile $afterFile 2>$null
Write-Host $afterExpanded
$afterHasWarning = $afterExpanded -match 'Pick a file before saving'
Write-Host "Contains warning text: $afterHasWarning (expected True)" -ForegroundColor $(if ($afterHasWarning) { 'Green' } else { 'Red' })
