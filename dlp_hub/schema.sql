CREATE DATABASE aerologue_dlp;
USE aerologue_dlp;

CREATE TABLE companies (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    tenant_key VARCHAR(100) UNIQUE
);

CREATE TABLE device_policies (
    id INT AUTO_INCREMENT PRIMARY KEY,
    company_id INT,
    machine_name VARCHAR(255) UNIQUE,
    block_usb TINYINT(1) DEFAULT 0,
    block_cd TINYINT(1) DEFAULT 0,
    block_bt TINYINT(1) DEFAULT 0,
    tracked_folders JSON,
    banned_keywords JSON,
    enforcement_mode VARCHAR(10) DEFAULT 'WARN',
    FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE telemetry_events (
    id INT AUTO_INCREMENT PRIMARY KEY,
    company_id INT,
    machine_name VARCHAR(255),
    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    event_type VARCHAR(50),
    details TEXT,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);

-- Insert a test tenant and policy
INSERT INTO companies (name, tenant_key) VALUES ('Admin Corp', 'CORP_ABC_123');
INSERT INTO device_policies (company_id, machine_name, block_usb, block_cd, block_bt, tracked_folders, banned_keywords) 
VALUES (1, 'AJMAL', 1, 1, 1, '["D:\\\\Confidential"]', '["project_zeus", "password"]');