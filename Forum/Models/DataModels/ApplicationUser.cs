﻿using Forum.Enums;
using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;

namespace Forum.Models.DataModels {
	public class ApplicationUser : IdentityUser {
		[Required]
		[StringLength(64)]
		public string DisplayName { get; set; }
		public DateTime Registered { get; set; } = new DateTime(2000, 1, 1);
		public DateTime LastOnline { get; set; } = new DateTime(2000, 1, 1);
		public string AvatarPath { get; set; }

		public bool ShowBirthday { get; set; }
		public DateTime Birthday { get; set; } = new DateTime(1980, 1, 1);
		public EFrontPage FrontPage { get; set; }
		public int MessagesPerPage { get; set; }
		public int PopularityLimit { get; set; }
		public bool Poseys { get; set; }
		public bool ShowFavicons { get; set; }
		public int TopicsPerPage { get; set; }
	}
}