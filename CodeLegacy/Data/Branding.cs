namespace Flowframes.Data
{
    /// <summary>
    /// Identidade do projeto "Flow Frames Brasil". Para trocar o repositorio de atualizacoes,
    /// altere somente <see cref="RepoOwner"/> e <see cref="RepoName"/>.
    /// </summary>
    public static class Branding
    {
        public const string AppName = "Flow Frames Brasil";
        public const string RepoOwner = "leaomoacir";
        public const string RepoName = "flowframes-brasil";

        public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
        public const string IssuesUrl = RepoUrl + "/issues";
        public const string ReleasesUrl = RepoUrl + "/releases";
        public const string LatestReleaseApi = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";
        public const string ChangelogRawUrl = "https://raw.githubusercontent.com/" + RepoOwner + "/" + RepoName + "/main/changelog.txt";

        // Creditos do projeto original (licenca GPLv3)
        public const string UpstreamName = "Flowframes, de N00MKRAD";
        public const string UpstreamUrl = "https://github.com/n00mkrad/flowframes";
    }
}
