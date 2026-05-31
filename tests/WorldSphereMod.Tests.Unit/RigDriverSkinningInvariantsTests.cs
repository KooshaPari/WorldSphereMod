using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

/// <summary>
/// FR-WSM-008: SkeletalAnimation — re-enabled skinned humanoid rig invariants.
///
/// RigDriver depends on UnityEngine types (SkinnedMeshRenderer, GameObject) that
/// are unavailable on CI runners, so — like HumanoidRigBindPoseTests — these are
/// static-source guards. They lock in the two properties that make the skinned
/// path SOUND rather than the shredding rig that was previously disabled:
///   1. bind-pose space consistency: the mesh bindposes and the bone Transform
///      hierarchy are BOTH derived from the same per-bone voxel CENTROIDS, so the
///      rest skin matrix is identity and nothing swings about a foreign pivot.
///   2. skin weights are BLENDED across &lt;=4 bones and NORMALIZED to sum 1, so
///      bone rotation bends seams instead of tearing them.
/// </summary>
public class RigDriverSkinningInvariantsTests
{
    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    static string ReadRigDriver() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(),
            "WorldSphereMod", "Code", "Rig", "RigDriver.cs"));

    [Fact]
    public void SkinnedRig_is_production_ready_enabled()
    {
        var src = ReadRigDriver();
        Regex.IsMatch(src, @"const bool kSkinnedRigProductionReady = true;").Should().BeTrue(
            "the skinned humanoid path is re-enabled now that bind poses are mesh-aligned");
    }

    [Fact]
    public void BindPoses_are_derived_from_per_bone_centroids_not_hardcoded_unit_offsets()
    {
        var src = ReadRigDriver();
        // Bind position is the centroid (sum / count) of the voxels a bone skins.
        src.Should().Contain("sum[b] / cnt[b]",
            "each bone joint must sit at the centroid of its assigned voxels (mesh space)");
        // Bindpose is the inverse translation of that centroid → rest matrix == identity.
        src.Should().Contain("Matrix4x4.Translate(-boneBindWorld[b])",
            "mesh bindpose must be the inverse of the centroid translation so rest skin == identity");
        // The bone Transform hierarchy is positioned from the SAME centroids.
        src.Should().Contain("boneBindWorld[i] - parentWorld",
            "bone Transform localPosition must come from the same centroids as the bindposes");
    }

    [Fact]
    public void SkinWeights_are_blended_and_normalized()
    {
        var src = ReadRigDriver();
        // Two influences (own bone + parent), each weight assigned.
        src.Should().Contain("weights[i].boneIndex1 = parent;",
            "vertices must blend to a second (parent) bone, not be rigidly single-weighted");
        // Normalization factor applied to both weights → sum is 1 (<=4 influences,
        // here exactly 2, which satisfies Unity's 4-bone BoneWeight cap).
        src.Should().Contain("float inv = 1f / (wOwn + wPar);",
            "blended weights must be normalized so weight0 + weight1 == 1");
        src.Should().Contain("weights[i].weight0 = wOwn * inv;");
        src.Should().Contain("weights[i].weight1 = wPar * inv;");
    }

    [Fact]
    public void Non_humanoid_rigs_still_fall_back_to_static_voxel_submit()
    {
        var src = ReadRigDriver();
        // Re-enabling must NOT break the static fallback for quadruped/snake/etc.
        src.Should().Contain("rigType != RigType.Humanoid",
            "only humanoid rigs use the skinned hierarchy; others stay static");
        src.Should().Contain("return VoxelRender.Submit(svm.BaseMesh, Matrix4x4.TRS(pos, rot, scl), tint)",
            "non-humanoid rigs must still submit the static voxel mesh");
    }
}
