# Docs deployment notes

The public docs site is at https://world-sphere-mod-docs.vercel.app/.
After updating ADRs, run one of the paths below to publish the latest content.

## 1) Preferred: redeploy from CLI

From the repo root:

1. Install and sign in to Vercel CLI (if needed):

```bash
npm install -g vercel
vercel login
```

2. Run a production deploy from the docs directory:

```bash
cd docs
vercel link             # one-time project link
vercel deploy --prod
```

If deployment is tied to git, you can also run:

```bash
git status                  # confirm ADR changes are committed/pushed
git add docs/adr ...
git commit -m "docs: update ADRs"
git push
```

Vercel will auto-redeploy from the connected branch if hooks are configured.

## 2) Manual dashboard fallback

Use this when the CLI is unavailable:

1. Open the Vercel project for `world-sphere-mod-docs`.
2. Go to **Deployments**.
3. Find the latest successful deployment and choose **Redeploy** (or trigger the deploy hook if available).
4. Wait for the deployment to finish and confirm https://world-sphere-mod-docs.vercel.app/ reflects the latest ADR changes.
