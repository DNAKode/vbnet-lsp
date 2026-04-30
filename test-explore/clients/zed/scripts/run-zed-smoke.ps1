param(
    [string]$ZedPath = 'zed',
    [string]$WorkspacePath = 'test/TestProjects/SmallProject'
)

$ErrorActionPreference = 'Stop'

throw "Zed smoke automation is not implemented yet. Install the dev extension manually, run '$ZedPath --foreground $WorkspacePath', and follow test-explore/clients/zed/README.md."
