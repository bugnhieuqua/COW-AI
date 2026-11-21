import pg from 'pg';
const { Client } = pg;

async function check() {
  const c = new Client('postgresql://postgres:postgres@localhost:5432/cowai_db');
  await c.connect();
  const res = await c.query('SELECT "Id", "Title", "PlanMarkdown", "SourceFileName", "SourceMarkdown" FROM "Projects" WHERE "Id" = 10');
  const p = res.rows[0];
  console.log('Project Id:', p.Id);
  console.log('Title:', p.Title);
  console.log('SourceFileName:', p.SourceFileName);
  console.log('SourceMarkdown length:', (p.SourceMarkdown || '').length);
  console.log('PlanMarkdown:\n', p.PlanMarkdown);
  await c.end();
}

check().catch(console.error);
