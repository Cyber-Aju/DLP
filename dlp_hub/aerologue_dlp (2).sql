-- Adminer 4.8.2-dev MySQL 8.0.39 dump

SET NAMES utf8;
SET time_zone = '+00:00';
SET foreign_key_checks = 0;
SET sql_mode = 'NO_AUTO_VALUE_ON_ZERO';

SET NAMES utf8mb4;

DROP TABLE IF EXISTS `companies`;
CREATE TABLE `companies` (
  `id` int NOT NULL AUTO_INCREMENT,
  `name` varchar(255) DEFAULT NULL,
  `tenant_key` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `tenant_key` (`tenant_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO `companies` (`id`, `name`, `tenant_key`) VALUES
(1,	'Admin Corp',	'CORP_ABC_123'),
(2,	'Stark Industry',	'eyJvcmciOiJTdGFyayBJbmR1c3RyeSIsImV4cGlyeSI6IjIwMjYtMDUtMDlUMjM6NTk6NTlaIn0=.I9Z9ekueR9wlRWnwFooiMla5WK1LuxhR6URJZc/9MQI=');

DROP TABLE IF EXISTS `device_policies`;
CREATE TABLE `device_policies` (
  `id` int NOT NULL AUTO_INCREMENT,
  `company_id` int DEFAULT NULL,
  `machine_name` varchar(255) DEFAULT NULL,
  `block_usb` tinyint(1) DEFAULT '0',
  `block_cd` tinyint(1) DEFAULT '0',
  `block_bt` tinyint(1) DEFAULT '0',
  `tracked_folders` json DEFAULT NULL,
  `banned_keywords` json DEFAULT NULL,
  `enforcement_mode` varchar(10) DEFAULT 'WARN',
  `upload_blocked` tinyint DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `machine_name` (`machine_name`),
  KEY `company_id` (`company_id`),
  CONSTRAINT `device_policies_ibfk_1` FOREIGN KEY (`company_id`) REFERENCES `companies` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO `device_policies` (`id`, `company_id`, `machine_name`, `block_usb`, `block_cd`, `block_bt`, `tracked_folders`, `banned_keywords`, `enforcement_mode`, `upload_blocked`) VALUES
(1,	2,	'AJMAL',	1,	1,	1,	'[\"D:\\\\Confidential\"]',	'[\"project_zeus\", \"password\"]',	'WARN',	1),
(2,	1,	'DEFAULT',	1,	1,	1,	'[]',	'[]',	'WARN',	1);

DROP TABLE IF EXISTS `telemetry_events`;
CREATE TABLE `telemetry_events` (
  `id` int NOT NULL AUTO_INCREMENT,
  `company_id` int DEFAULT NULL,
  `machine_name` varchar(255) DEFAULT NULL,
  `timestamp` datetime DEFAULT CURRENT_TIMESTAMP,
  `event_type` varchar(50) DEFAULT NULL,
  `details` text,
  PRIMARY KEY (`id`),
  KEY `company_id` (`company_id`),
  CONSTRAINT `telemetry_events_ibfk_1` FOREIGN KEY (`company_id`) REFERENCES `companies` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- 2026-05-05 17:50:29
