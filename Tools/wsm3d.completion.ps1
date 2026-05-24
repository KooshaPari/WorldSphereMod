# PowerShell tab-completion for wsm3d CLI
#
# Install: add this to your $PROFILE (run `notepad $PROFILE`):
#   . "$env:USERPROFILE\Dev\WorldSphereMod\Tools\wsm3d.completion.ps1"
#
# Or one-liner from the repo root:
#   Add-Content $PROFILE "`n. `"$pwd\Tools\wsm3d.completion.ps1`""

Register-ArgumentCompleter -Native -CommandName "wsm3d.ps1", "wsm3d" -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $elements = @($commandAst.CommandElements | ForEach-Object { $_.ToString() })

    # $elements[0] = script name, [1] = first arg (subcommand), [2] = second arg, etc.
    $arg1 = if ($elements.Count -ge 2) { $elements[1] } else { "" }
    $arg2 = if ($elements.Count -ge 3) { $elements[2] } else { "" }
    $arg3 = if ($elements.Count -ge 4) { $elements[3] } else { "" }

    # Phase slugs and their PascalCase equivalents
    $phaseSlug = @(
        "voxel_entities",
        "procedural_buildings",
        "crossed_quad_foliage",
        "mesh_water",
        "high_shadows",
        "skeletal_animation",
        "worldspace_ui",
        "day_night_cycle",
        "post_fx",
        "particle_effects"
    )

    $phasePascalCase = @(
        "VoxelEntities",
        "ProceduralBuildings",
        "CrossedQuadFoliage",
        "MeshWater",
        "HighShadows",
        "SkeletalAnimation",
        "WorldspaceUI",
        "DayNightCycle",
        "PostFX",
        "ParticleEffects"
    )

    # Main subcommands
    $mainCommands = @(
        "build", "install", "launch", "kill", "relaunch", "log",
        "screenshot", "settings", "toggle", "phases", "status", "doctor",
        "journey", "playcua", "watch", "hooks", "submodule", "help"
    )

    # If we're at the first argument position (completing subcommand)
    if ($elements.Count -le 2) {
        return $mainCommands |
            Where-Object { $_ -like "$wordToComplete*" } |
            ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
    }

    # Handle second-level and deeper arguments based on the subcommand
    switch ($arg1) {
        "settings" {
            # settings <get|set>
            if ($elements.Count -le 3) {
                @("get", "set") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            # settings get/set -Key <phase>
            elseif ($arg2 -in @("get", "set") -and $arg3 -eq "-Key") {
                return $phasePascalCase |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            elseif ($arg2 -in @("get", "set") -and $arg3 -like "-Key*") {
                # Suggest -Key flag
                return @("-Key") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
        }

        "phases" {
            # phases <list|enable-all|preset>
            if ($elements.Count -le 3) {
                @("list", "enable-all", "preset") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            elseif ($arg2 -eq "preset" -and $elements.Count -le 4) {
                @("safe-min") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
        }

        "journey" {
            # journey <verify|capture>
            if ($elements.Count -le 3) {
                @("verify", "capture") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            # journey verify/capture -Id <journey-id> [-NonInteractive]
            elseif ($arg2 -in @("verify", "capture")) {
                if ($arg2 -eq "capture" -and $elements.Count -le 4 -and $arg3 -notlike "-*") {
                    @("-Id", "-NonInteractive") |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                        }
                }
                elseif ($arg3 -eq "-Id") {
                    # Load journey IDs from manifest
                    $journeyIds = @()
                    $manifestPath = "$PSScriptRoot\..\docs\journeys\manifests\index.json"
                    if (Test-Path $manifestPath) {
                        try {
                            $manifests = Get-Content $manifestPath | ConvertFrom-Json
                            $journeyIds = $manifests | ForEach-Object { $_.id }
                        }
                        catch {
                            # Silently fail; return empty
                        }
                    }
                    return $journeyIds |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                        }
                }
                elseif ($arg3 -like "-Id*") {
                    # Suggest -Id flag
                    return @("-Id") |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                        }
                }
            }
        }

        "toggle" {
            # toggle -Phase <phase-slug>
            if ($arg2 -eq "-Phase") {
                return $phaseSlug |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            elseif ($arg2 -like "-Phase*") {
                # Suggest -Phase flag
                return @("-Phase") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
        }

        "playcua" {
            # playcua <run-all>
            if ($elements.Count -le 3) {
                @("run-all") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            elseif ($arg2 -eq "run-all") {
                if ($arg3 -eq "-VisionBackend") {
                    @("omniroute", "anthropic", "off") |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                        }
                }
                else {
                    @("-VisionBackend") |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                        }
                }
            }
        }

        "doctor" {
            if ($elements.Count -le 3) {
                @("-Json") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
        }

        "submodule" {
            if ($elements.Count -le 3) {
                @("init") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
        }

        "screenshot" {
            # screenshot <phase> | -Path ...
            if ($elements.Count -le 3) {
                @("phase") |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
            }
            elseif ($arg2 -eq "phase") {
                if ($elements.Count -le 4) {
                    1..10 | ForEach-Object { "$_" } |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                        }
                }
                elseif ($arg3 -match '^\d+$') {
                    $phaseNum = [int]$arg3
                    $phaseCloseup = switch ($phaseNum) {
                        1 { "buildings" }
                        2 { "buildings" }
                        3 { "foliage" }
                        4 { "water" }
                        5 { "shadows-sky" }
                        default { $null }
                    }
                    if ($arg4 -eq "-Name") {
                        $names = if ($phaseCloseup) {
                            @("before", "after", $phaseCloseup)
                        } else {
                            @("before", "after")
                        }
                        return $names |
                            Where-Object { $_ -like "$wordToComplete*" } |
                            ForEach-Object {
                                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                            }
                    }
                    elseif ($elements.Count -le 5 -and $arg4 -notlike "-*") {
                        $names = if ($phaseCloseup) {
                            @("before", "after", $phaseCloseup)
                        } else {
                            @("before", "after")
                        }
                        return $names |
                            Where-Object { $_ -like "$wordToComplete*" } |
                            ForEach-Object {
                                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                            }
                    }
                    else {
                        @("-Name", "-WindowOnly") |
                            Where-Object { $_ -like "$wordToComplete*" } |
                            ForEach-Object {
                                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                            }
                    }
                }
            }
        }
    }

    return @()
}
