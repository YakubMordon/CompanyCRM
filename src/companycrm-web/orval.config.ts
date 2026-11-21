import { defineConfig } from 'orval';

export default defineConfig({
  companycrm: {
    input: {
      target: 'http://localhost:5000/swagger/v1/swagger.json',
    },
    output: {
      mode: 'tags-split',
      target: 'src/api/generated',
      schemas: 'src/api/generated/model',
      client: 'react-query',
      mock: false,
      override: {
        mutator: {
          path: './src/api/mutator.ts',
          name: 'customInstance',
        },
      },
    },
  },
});
