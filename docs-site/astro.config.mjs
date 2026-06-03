// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Featly documentation site.
// Hosted on the GitHub Pages project URL, so `site` + `base` point at the
// repository sub-path. Switching to a custom domain later is a config-only
// change (set `site` to the domain and `base` to '/'). See ADR-0025.
export default defineConfig({
  site: 'https://featly-net.github.io',
  base: '/Featly',
  integrations: [
    starlight({
      title: 'Featly',
      description:
        'Feature management for .NET — feature flags, dynamic configuration, ' +
        'segments, experiments, and enterprise governance, embedded in your ' +
        'ASP.NET Core app like Hangfire.',
      logo: {
        src: './src/assets/logo.svg',
        alt: 'Featly',
      },
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/Featly-net/Featly',
        },
      ],
      editLink: {
        baseUrl: 'https://github.com/Featly-net/Featly/edit/main/docs-site/',
      },
      lastUpdated: true,
      customCss: ['./src/styles/custom.css'],
      sidebar: [
        {
          label: 'Start here',
          items: [
            { label: 'Introduction', slug: 'introduction' },
            { label: 'Getting started', slug: 'getting-started' },
          ],
        },
        {
          label: 'Concepts',
          items: [
            { label: 'Flags and configuration', slug: 'concepts/flags-and-configs' },
            { label: 'Targeting and rules', slug: 'concepts/targeting' },
            { label: 'Segments and experiments', slug: 'concepts/segments-and-experiments' },
            { label: 'Projects and environments', slug: 'concepts/projects-and-environments' },
            { label: 'Governance', slug: 'concepts/governance' },
          ],
        },
        {
          label: 'Dashboard',
          items: [{ label: 'Dashboard tour', slug: 'dashboard' }],
        },
        {
          label: 'Operate',
          items: [
            { label: 'Configuration', slug: 'operate/configuration' },
            { label: 'Deployment', slug: 'operate/deployment' },
            { label: 'CLI', slug: 'operate/cli' },
            { label: 'Modularity', slug: 'operate/modularity' },
          ],
        },
        {
          label: 'Integrate',
          items: [{ label: 'OpenFeature', slug: 'integrate/openfeature' }],
        },
        {
          label: 'Reference',
          items: [
            { label: 'Performance', slug: 'reference/performance' },
            { label: 'Security', slug: 'reference/security' },
          ],
        },
      ],
    }),
  ],
});
