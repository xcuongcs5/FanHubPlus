<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Str;

class FandomGroupMember extends Model
{
    use HasFactory;

    protected $table = 'fandom_group_members';

    public $timestamps = false;

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'group_id',
        'user_id',
        'role',
        'status',
        'joined_at',
    ];

    protected static function booted(): void
    {
        static::creating(function (FandomGroupMember $member) {
            if (empty($member->id)) {
                $member->id = 'req_' . Str::lower(Str::random(12));
            }
            if (empty($member->joined_at)) {
                $member->joined_at = now();
            }
        });
    }

    public function group(): BelongsTo
    {
        return $this->belongsTo(FandomGroup::class, 'group_id');
    }
}
