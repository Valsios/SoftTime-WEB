using AutoMapper;

using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Role, RoleDto>().ForMember(d => d.Id, o => o.MapFrom(s => s.IdRole)).ForMember(d => d.Nom, o => o.MapFrom(s => s.NomRole));
        CreateMap<DroitAcces, DroitDto>().ForMember(d => d.Id, o => o.MapFrom(s => s.IdDroit)).ForMember(d => d.Nom, o => o.MapFrom(s => s.Droit));
        CreateMap<BddSage, SageDbDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.SqlAuth, o => o.MapFrom(s => s.TypeAuth))
            .ForMember(d => d.NomBd, o => o.MapFrom(s => s.NomBd))
            .ForMember(d => d.Password, o => o.Ignore())
            .ForMember(d => d.PasswordDefined, o => o.MapFrom(s => !string.IsNullOrEmpty(s.TMdp)))
            .ForMember(d => d.Login, o => o.MapFrom(s => s.TLogin))
            .ForMember(d => d.TypeBase, o => o.MapFrom(s => s.TypeBase))
            .ForMember(d => d.Mappings, o => o.Ignore());
        CreateMap<SageDbDto, BddSage>()
            .ForMember(d => d.TypeAuth, o => o.MapFrom(s => s.SqlAuth))
            .ForMember(d => d.NomBd, o => o.MapFrom(s => s.NomBd))
            .ForMember(d => d.TMdp, o => o.MapFrom(s => s.Password))
            .ForMember(d => d.TLogin, o => o.MapFrom(s => s.Login))
            .ForMember(d => d.Serveur, o => o.MapFrom(s => s.Serveur))
            .ForMember(d => d.TypeBase, o => o.MapFrom(s => s.TypeBase))
            // Colonnes de mapping historiques : gérées par la configuration des sources, jamais par le DTO.
            .ForMember(d => d.MapTable, o => o.Ignore())
            .ForMember(d => d.MapColMatricule, o => o.Ignore())
            .ForMember(d => d.MapColNom, o => o.Ignore())
            .ForMember(d => d.MapColPrenom, o => o.Ignore())
            .ForMember(d => d.MapColBadge, o => o.Ignore())
            .ForMember(d => d.MapColDepartement, o => o.Ignore())
            .ForMember(d => d.MapColService, o => o.Ignore())
            .ForMember(d => d.MapColCodeDepartement, o => o.Ignore())
            .ForMember(d => d.MapTableCodeConstante, o => o.Ignore())
            .ForMember(d => d.MapColCodeConstante, o => o.Ignore());
        CreateMap<BddPointeuse, PointeuseDbDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.SqlAuth, o => o.MapFrom(s => s.TypeAuth))
            .ForMember(d => d.NomBd, o => o.MapFrom(s => s.NomBd))
            .ForMember(d => d.Password, o => o.Ignore())
            .ForMember(d => d.PasswordDefined, o => o.MapFrom(s => !string.IsNullOrEmpty(s.TMdp)))
            .ForMember(d => d.Login, o => o.MapFrom(s => s.TLogin))
            .ForMember(d => d.TypePointage, o => o.MapFrom(s => s.TypePointage))
            .ForMember(d => d.Active, o => o.MapFrom(s => s.Active))
            .ForMember(d => d.TypeBase, o => o.MapFrom(s => s.TypeBase))
            .ForMember(d => d.Mappings, o => o.Ignore());
        CreateMap<FieldRole, FieldRoleDto>();
        CreateMap<SourceFieldMapping, SourceFieldMappingDto>();
        CreateMap<SourceEntityMapping, SourceEntityMappingDto>();
        CreateMap<SourceFieldMappingDto, SourceFieldMapping>()
            // Clé étrangère posée par le service, pas par le DTO.
            .ForMember(d => d.EntityMappingId, o => o.Ignore());
        CreateMap<SourceEntityMappingDto, SourceEntityMapping>();
        CreateMap<PointeuseDbDto, BddPointeuse>()
            .ForMember(d => d.TypeAuth, o => o.MapFrom(s => s.SqlAuth))
            .ForMember(d => d.NomBd, o => o.MapFrom(s => s.NomBd))
            .ForMember(d => d.TMdp, o => o.MapFrom(s => s.Password))
            .ForMember(d => d.TLogin, o => o.MapFrom(s => s.Login))
            .ForMember(d => d.TypePointage, o => o.MapFrom(s => s.TypePointage))
            .ForMember(d => d.Active, o => o.MapFrom(s => s.Active))
            .ForMember(d => d.Serveur, o => o.MapFrom(s => s.Serveur))
            .ForMember(d => d.TypeBase, o => o.MapFrom(s => s.TypeBase))
            // Colonnes de mapping historiques : gérées par la configuration des sources, jamais par le DTO.
            .ForMember(d => d.MapUserTable, o => o.Ignore())
            .ForMember(d => d.MapUserColId, o => o.Ignore())
            .ForMember(d => d.MapUserColBadge, o => o.Ignore())
            .ForMember(d => d.MapUserColSsn, o => o.Ignore())
            .ForMember(d => d.MapUserColNom, o => o.Ignore())
            .ForMember(d => d.MapPunchTable, o => o.Ignore())
            .ForMember(d => d.MapPunchColUserId, o => o.Ignore())
            .ForMember(d => d.MapPunchColDatetime, o => o.Ignore())
            .ForMember(d => d.MapPunchColType, o => o.Ignore());
        CreateMap<CardPaie, CardPaieDto>()
            .ForMember(d => d.SageMatricule, o => o.MapFrom(s => s.SageMatricule))
            .ForMember(d => d.SageNom, o => o.MapFrom(s => s.SageNom))
            .ForMember(d => d.SagePrenom, o => o.MapFrom(s => s.SagePrenom))
            .ForMember(d => d.PointeuseNumero, o => o.MapFrom(s => s.PointeuseNumero))
            .ForMember(d => d.PointeuseNom, o => o.MapFrom(s => s.PointeuseNom))
            .ForMember(d => d.SageServeur, o => o.MapFrom(s => s.SageServeur))
            .ForMember(d => d.PointeuseServeur, o => o.MapFrom(s => s.PointeuseServeur))
            .ForMember(d => d.SageBdd, o => o.MapFrom(s => s.SageBdd))
            .ForMember(d => d.PointeuseBdd, o => o.MapFrom(s => s.PointeuseBdd))
            .ForMember(d => d.Origine, o => o.MapFrom(s => s.Origine))
            .ForMember(d => d.Actif, o => o.MapFrom(s => s.Actif))
            .ForMember(d => d.Departement, o => o.MapFrom(s => s.Departement));
        CreateMap<CardPaieDto, CardPaie>()
            .ForMember(d => d.SageMatricule, o => o.MapFrom(s => s.SageMatricule))
            .ForMember(d => d.SageNom, o => o.MapFrom(s => s.SageNom))
            .ForMember(d => d.SagePrenom, o => o.MapFrom(s => s.SagePrenom))
            .ForMember(d => d.PointeuseNumero, o => o.MapFrom(s => s.PointeuseNumero))
            .ForMember(d => d.PointeuseNom, o => o.MapFrom(s => s.PointeuseNom))
            .ForMember(d => d.SageServeur, o => o.MapFrom(s => s.SageServeur))
            .ForMember(d => d.PointeuseServeur, o => o.MapFrom(s => s.PointeuseServeur))
            .ForMember(d => d.SageBdd, o => o.MapFrom(s => s.SageBdd))
            .ForMember(d => d.PointeuseBdd, o => o.MapFrom(s => s.PointeuseBdd))
            .ForMember(d => d.Departement, o => o.MapFrom(s => s.Departement));
        CreateMap<Categorie, CategoryDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.IdCategorie))
            .ForMember(d => d.SupervisorCardId, o => o.MapFrom(s => s.IdCardPaie))
            .ForMember(d => d.HeuresSemaine, o => o.MapFrom(s => s.HeureSemaine));
        CreateMap<Majoration, MajorationDto>();
        CreateMap<CodeConstanteCategorie, CodeConstanteDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.Categorie, o => o.MapFrom(s => s.Categorie))
            .ForMember(d => d.Intitule, o => o.MapFrom(s => s.Intitule))
            .ForMember(d => d.CodeConstante, o => o.MapFrom(s => s.CodeConstante))
            // La table cible n'est plus stockee : elle provient du mapping « Code constante ».
            .ForMember(d => d.TableCible, o => o.Ignore());
        CreateMap<JourFerie, HolidayDto>();
        CreateMap<Planning, ShiftDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.IdPlanning))
            .ForMember(d => d.NoShift, o => o.MapFrom(s => s.NoShift))
            .ForMember(d => d.Ha, o => o.MapFrom(s => s.Ha))
            .ForMember(d => d.Hd, o => o.MapFrom(s => s.Hd));
    }
}
