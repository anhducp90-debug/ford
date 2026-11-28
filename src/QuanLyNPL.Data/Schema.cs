namespace QuanLyNPL.Data;

/// <summary>Danh sách migration có thứ tự; PRAGMA user_version = số migration đã áp dụng.</summary>
public static class Schema
{
    public static readonly string[] Migrations =
    {
@"
CREATE TABLE customer(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE material(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, unit TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, note TEXT);
CREATE TABLE size_grid(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE size_grid_item(id INTEGER PRIMARY KEY, grid_id INTEGER NOT NULL REFERENCES size_grid(id),
  size_code TEXT NOT NULL, voc TEXT, chieu_cao TEXT, UNIQUE(grid_id,size_code));
CREATE TABLE product(id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customer(id), code TEXT NOT NULL, name TEXT NOT NULL,
  default_grid_id INTEGER NOT NULL REFERENCES size_grid(id), active INTEGER NOT NULL DEFAULT 1, UNIQUE(customer_id,code));
CREATE TABLE variant(id INTEGER PRIMARY KEY, product_id INTEGER NOT NULL REFERENCES product(id), code TEXT NOT NULL, name TEXT NOT NULL,
  group_name TEXT, qty_per_set TEXT NOT NULL CHECK(CAST(qty_per_set AS REAL) > 0), grid_id INTEGER NOT NULL REFERENCES size_grid(id), UNIQUE(product_id,code));
CREATE TABLE size_map(id INTEGER PRIMARY KEY, variant_id INTEGER NOT NULL REFERENCES variant(id),
  default_size_code TEXT NOT NULL, variant_size_code TEXT NOT NULL, UNIQUE(variant_id,default_size_code));

CREATE TABLE bom(id INTEGER PRIMARY KEY, product_id INTEGER NOT NULL REFERENCES product(id), name TEXT NOT NULL, type TEXT,
  scope_variant_id INTEGER REFERENCES variant(id), UNIQUE(product_id,name));
CREATE TABLE bom_version(id INTEGER PRIMARY KEY, bom_id INTEGER NOT NULL REFERENCES bom(id), version_no INTEGER NOT NULL,
  created_at TEXT NOT NULL, UNIQUE(bom_id,version_no));
CREATE TABLE bom_line(id INTEGER PRIMARY KEY, bom_version_id INTEGER NOT NULL REFERENCES bom_version(id),
  material_id INTEGER NOT NULL REFERENCES material(id), size_code TEXT NOT NULL, usage TEXT NOT NULL, waste_percent TEXT NOT NULL DEFAULT '0',
  variant_id INTEGER REFERENCES variant(id));

CREATE TABLE ""order""(id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customer(id), product_id INTEGER NOT NULL REFERENCES product(id),
  order_no TEXT NOT NULL UNIQUE, order_date TEXT NOT NULL, due_date TEXT, status TEXT NOT NULL DEFAULT 'OPEN');
CREATE TABLE order_line(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id), default_size_code TEXT NOT NULL,
  sets_qty TEXT NOT NULL, UNIQUE(order_id,default_size_code));
CREATE TABLE order_snapshot(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id), created_at TEXT NOT NULL, config_json TEXT NOT NULL);

CREATE TABLE stock_doc(id INTEGER PRIMARY KEY, type TEXT NOT NULL CHECK(type IN ('IN','OUT','RETURN','ADJUST_UP','ADJUST_DOWN')),
  doc_no TEXT NOT NULL UNIQUE, status TEXT NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','CONFIRMED')),
  order_id INTEGER REFERENCES ""order""(id), note TEXT, created_at TEXT NOT NULL, confirmed_at TEXT);
CREATE TABLE stock_doc_line(id INTEGER PRIMARY KEY, doc_id INTEGER NOT NULL REFERENCES stock_doc(id),
  material_id INTEGER NOT NULL REFERENCES material(id), qty TEXT NOT NULL);
CREATE TABLE stock_txn(id INTEGER PRIMARY KEY, doc_id INTEGER NOT NULL REFERENCES stock_doc(id),
  material_id INTEGER NOT NULL REFERENCES material(id), qty_signed TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE TABLE reservation(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id),
  material_id INTEGER NOT NULL REFERENCES material(id), qty TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE carry_ledger(id INTEGER PRIMARY KEY, order_id INTEGER NOT NULL REFERENCES ""order""(id), material_id INTEGER NOT NULL REFERENCES material(id),
  scope TEXT NOT NULL, opening TEXT NOT NULL, issued TEXT NOT NULL, demand TEXT NOT NULL, closing TEXT NOT NULL, doc_id INTEGER REFERENCES stock_doc(id), created_at TEXT NOT NULL);
CREATE TABLE audit_log(id INTEGER PRIMARY KEY, entity TEXT NOT NULL, entity_id INTEGER, before_json TEXT, after_json TEXT, reason TEXT, created_at TEXT NOT NULL);
CREATE TABLE app_setting(key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE INDEX ix_variant_product ON variant(product_id);
CREATE INDEX ix_bom_product ON bom(product_id);
CREATE INDEX ix_bomline_version ON bom_line(bom_version_id);
CREATE INDEX ix_stocktxn_material ON stock_txn(material_id);
CREATE INDEX ix_order_customer ON ""order""(customer_id);
"
    ,
@"
ALTER TABLE stock_doc ADD COLUMN scope TEXT;
ALTER TABLE stock_doc ADD COLUMN ref_doc_id INTEGER REFERENCES stock_doc(id);
ALTER TABLE stock_doc_line ADD COLUMN qty_suggested TEXT;
ALTER TABLE stock_doc_line ADD COLUMN demand TEXT;
ALTER TABLE stock_doc_line ADD COLUMN entered INTEGER NOT NULL DEFAULT 1;
CREATE INDEX ix_carry_scope ON carry_ledger(order_id,material_id,scope);
CREATE INDEX ix_doc_order ON stock_doc(order_id,status);
CREATE TABLE stock_count(id INTEGER PRIMARY KEY, doc_no TEXT NOT NULL UNIQUE, status TEXT NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','CONFIRMED')),
  reason TEXT, created_at TEXT NOT NULL, confirmed_at TEXT);
CREATE TABLE stock_count_line(id INTEGER PRIMARY KEY, count_id INTEGER NOT NULL REFERENCES stock_count(id),
  material_id INTEGER NOT NULL REFERENCES material(id), counted TEXT NOT NULL, book_qty TEXT, diff TEXT, UNIQUE(count_id,material_id));
"
    ,
@"
ALTER TABLE material ADD COLUMN customer_id INTEGER REFERENCES customer(id);
CREATE INDEX ix_material_customer ON material(customer_id);
"
    ,
@"
ALTER TABLE stock_doc ADD COLUMN customer_id INTEGER REFERENCES customer(id);
ALTER TABLE stock_doc ADD COLUMN kind TEXT;
ALTER TABLE stock_count ADD COLUMN customer_id INTEGER REFERENCES customer(id);
UPDATE stock_doc SET customer_id=(SELECT customer_id FROM ""order"" o WHERE o.id=stock_doc.order_id) WHERE order_id IS NOT NULL;
UPDATE stock_doc SET customer_id=(SELECT MIN(m.customer_id) FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE l.doc_id=stock_doc.id)
  WHERE customer_id IS NULL AND (SELECT COUNT(DISTINCT m.customer_id) FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE l.doc_id=stock_doc.id)=1;
CREATE INDEX ix_doc_customer ON stock_doc(customer_id);
CREATE UNIQUE INDEX ux_bom_variant_type ON bom(scope_variant_id,type) WHERE scope_variant_id IS NOT NULL AND type IN ('TO_CAT','CHUNG_LOAI','DONG_GOI');
CREATE TABLE material_transfer(id INTEGER PRIMARY KEY, doc_no TEXT NOT NULL UNIQUE,
  from_customer_id INTEGER NOT NULL REFERENCES customer(id), to_customer_id INTEGER NOT NULL REFERENCES customer(id),
  out_doc_id INTEGER NOT NULL REFERENCES stock_doc(id), in_doc_id INTEGER NOT NULL REFERENCES stock_doc(id),
  reason TEXT, status TEXT NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','CONFIRMED')), created_at TEXT NOT NULL, confirmed_at TEXT);
CREATE TABLE material_transfer_line(id INTEGER PRIMARY KEY, transfer_id INTEGER NOT NULL REFERENCES material_transfer(id),
  from_material_id INTEGER NOT NULL REFERENCES material(id), to_material_id INTEGER NOT NULL REFERENCES material(id), qty TEXT NOT NULL);
"
    ,
@"
ALTER TABLE bom ADD COLUMN per_set INTEGER NOT NULL DEFAULT 0;
DROP INDEX ux_bom_variant_type;
UPDATE bom SET type='SAN_XUAT', name=replace(name,' – Chủng loại',' – Sản xuất') WHERE type='CHUNG_LOAI';
CREATE UNIQUE INDEX ux_bom_variant_type ON bom(scope_variant_id,type) WHERE scope_variant_id IS NOT NULL AND type IN ('TO_CAT','SAN_XUAT','DONG_GOI');
CREATE UNIQUE INDEX ux_bom_set_type ON bom(product_id,type) WHERE per_set=1;
"
    ,
@"
-- Bỏ quy đổi size: mọi chủng loại dùng lưới size của mã hàng. Bảng size_map giữ lại để tra cứu lịch sử, không dùng nữa.
UPDATE variant SET grid_id=(SELECT p.default_grid_id FROM product p WHERE p.id=variant.product_id);
"
    ,
@"
CREATE TABLE place(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, address TEXT,
  kind TEXT NOT NULL DEFAULT 'CA_HAI' CHECK(kind IN ('XUAT','NHAN','CA_HAI')), active INTEGER NOT NULL DEFAULT 1);
ALTER TABLE stock_doc ADD COLUMN from_place_id INTEGER REFERENCES place(id);
ALTER TABLE stock_doc ADD COLUMN to_place_id INTEGER REFERENCES place(id);
"
    };
}
