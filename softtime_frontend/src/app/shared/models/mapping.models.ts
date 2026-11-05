export interface FieldRole {
  code: string;
  label?: string | null;
  systemType: string;
  entityKind: string;
  isRequired: boolean;
  autoMappingPatterns?: string | null;
  sortOrder: number;
}

export interface SourceFieldMapping {
  id?: number;
  fieldRoleCode: string;
  sourceColumn?: string | null;
}

export interface SourceEntityMapping {
  id?: number;
  systemType?: string;
  entityKind: string;
  sourceTable?: string | null;
  fields: SourceFieldMapping[];
}
